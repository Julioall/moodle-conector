using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleParticipantsGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleParticipantsGateway
{
    private const int ParticipantFetchBatchSize = 100;
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<CourseParticipantsPage> GetCourseParticipantsAsync(
        string userExternalId,
        string courseId,
        ParticipantStatusFilter statusFilter,
        int page,
        int pageSize,
        bool studentsOnly,
        bool includeEmail,
        string? groupId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var normalizedCourseId = ParseMoodleId(courseId, "courseId");
        var normalizedGroupId = string.IsNullOrWhiteSpace(groupId)
            ? (int?)null
            : ParseMoodleId(groupId, "groupId");
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await ResolveReadTokenAsync(cancellationToken);

        var targetSkip = (page - 1) * pageSize;
        var skipped = 0;
        var fetchOffset = 0;
        var participants = new List<CourseParticipantSummary>(pageSize + 1);
        var evaluatedCount = 0;
        var includedByStudentRoleCount = 0;
        var includedByFallbackCount = 0;
        var excludedKnownStaffCount = 0;
        var hasEmptyRoles = false;
        var hasEmptyGroups = false;

        while (participants.Count < pageSize + 1)
        {
            var moodleParticipants = await GetParticipantsBatchAsync(
                credentials.BaseUrl,
                token,
                normalizedCourseId,
                statusFilter,
                includeEmail,
                normalizedGroupId,
                fetchOffset,
                ParticipantFetchBatchSize,
                cancellationToken);

            if (moodleParticipants.Count == 0)
            {
                break;
            }

            foreach (var participant in moodleParticipants.Select(dto => ToParticipant(dto, includeEmail)))
            {
                if (!MatchesStatus(participant, statusFilter))
                {
                    continue;
                }

                evaluatedCount++;
                hasEmptyRoles |= participant.Roles.Count == 0;
                hasEmptyGroups |= participant.Groups.Count == 0;

                var classification = ParticipantClassification.Classify(participant);
                if (studentsOnly && classification == ParticipantClassificationKind.KnownStaff)
                {
                    excludedKnownStaffCount++;
                    continue;
                }

                if (classification == ParticipantClassificationKind.Student)
                {
                    includedByStudentRoleCount++;
                }
                else if (classification == ParticipantClassificationKind.UncertainFallback)
                {
                    includedByFallbackCount++;
                }

                if (skipped < targetSkip)
                {
                    skipped++;
                    continue;
                }

                participants.Add(participant);
                if (participants.Count == pageSize + 1)
                {
                    break;
                }
            }

            fetchOffset += moodleParticipants.Count;
            if (moodleParticipants.Count < ParticipantFetchBatchSize)
            {
                break;
            }
        }

        var hasMore = participants.Count > pageSize;
        return new CourseParticipantsPage(
            normalizedCourseId.ToString(CultureInfo.InvariantCulture),
            page,
            pageSize,
            statusFilter,
            studentsOnly,
            includeEmail,
            hasMore,
            participants.Take(pageSize).ToArray(),
            new ParticipantClassificationDiagnostics(
                evaluatedCount,
                includedByStudentRoleCount,
                includedByFallbackCount,
                excludedKnownStaffCount,
                hasEmptyRoles,
                hasEmptyGroups,
                ResolveClassificationMode(
                    evaluatedCount,
                    includedByStudentRoleCount,
                    includedByFallbackCount)));
    }

    public async Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var normalizedCourseId = ParseMoodleId(courseId, "courseId");
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await ResolveReadTokenAsync(cancellationToken);

        var endpoint = BuildMoodleGetUrl(
            credentials.BaseUrl,
            token,
            "core_group_get_course_groups",
            new Dictionary<string, string>
            {
                ["courseid"] = normalizedCourseId.ToString(CultureInfo.InvariantCulture)
            });

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<GroupDto>>(cancellationToken: cancellationToken);
        return (payload ?? [])
            .Select(group => new CourseGroupSummary(
                ToIdString(group.Id),
                ToIdString(group.CourseId),
                group.Name ?? string.Empty,
                string.IsNullOrWhiteSpace(group.IdNumber) ? null : group.IdNumber))
            .ToArray();
    }

    private async Task<IReadOnlyList<ParticipantDto>> GetParticipantsBatchAsync(
        string baseUrl,
        string token,
        int courseId,
        ParticipantStatusFilter statusFilter,
        bool includeEmail,
        int? groupId,
        int limitFrom,
        int limitNumber,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["courseid"] = courseId.ToString(CultureInfo.InvariantCulture)
        };
        var options = new List<(string Name, string Value)>
        {
            ("limitfrom", limitFrom.ToString(CultureInfo.InvariantCulture)),
            ("limitnumber", limitNumber.ToString(CultureInfo.InvariantCulture)),
            ("userfields", BuildUserFields(includeEmail))
        };

        if (statusFilter == ParticipantStatusFilter.Active)
        {
            options.Add(("onlyactive", "1"));
        }

        if (groupId is not null)
        {
            options.Add(("groupid", groupId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        AddMoodleOptions(parameters, options);

        var endpoint = BuildMoodleGetUrl(
            baseUrl,
            token,
            "core_enrol_get_enrolled_users",
            parameters);

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<ParticipantDto>>(cancellationToken: cancellationToken);
        return payload ?? [];
    }

    private static CourseParticipantSummary ToParticipant(ParticipantDto dto, bool includeEmail)
    {
        return new CourseParticipantSummary(
            ToIdString(dto.Id),
            dto.FullName ?? string.Empty,
            includeEmail && !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : null,
            ToBool(dto.Suspended),
            ToDateTimeOffset(dto.FirstAccess),
            ToDateTimeOffset(dto.LastAccess),
            ToDateTimeOffset(dto.LastCourseAccess),
            (dto.Roles ?? [])
                .Select(role => new CourseParticipantRole(
                    ToIdString(role.RoleId),
                    string.IsNullOrWhiteSpace(role.ShortName) ? null : role.ShortName,
                    role.Name ?? string.Empty))
                .ToArray(),
            (dto.Groups ?? [])
                .Select(group => new CourseParticipantGroup(
                    ToIdString(group.Id),
                    group.Name ?? string.Empty))
                .ToArray());
    }

    private static bool MatchesStatus(CourseParticipantSummary participant, ParticipantStatusFilter statusFilter)
    {
        return statusFilter switch
        {
            ParticipantStatusFilter.Active => participant.Suspended is not true,
            ParticipantStatusFilter.Suspended => participant.Suspended is true,
            ParticipantStatusFilter.All => true,
            _ => true
        };
    }

    private static ParticipantClassificationMode ResolveClassificationMode(
        int evaluatedCount,
        int includedByStudentRoleCount,
        int includedByFallbackCount)
    {
        if (evaluatedCount == 0)
        {
            return ParticipantClassificationMode.NotRequested;
        }

        if (includedByFallbackCount > 0 && includedByStudentRoleCount > 0)
        {
            return ParticipantClassificationMode.Mixed;
        }

        return includedByFallbackCount > 0
            ? ParticipantClassificationMode.Fallback
            : ParticipantClassificationMode.RoleBased;
    }

    private static string BuildUserFields(bool includeEmail)
    {
        var fields = new List<string>
        {
            "id",
            "fullname",
            "suspended",
            "firstaccess",
            "lastaccess",
            "lastcourseaccess",
            "roles",
            "groups"
        };

        if (includeEmail)
        {
            fields.Add("email");
        }

        return string.Join(',', fields);
    }

    private static void AddMoodleOptions(
        IDictionary<string, string> parameters,
        IReadOnlyList<(string Name, string Value)> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            parameters[$"options[{i}][name]"] = options[i].Name;
            parameters[$"options[{i}][value]"] = options[i].Value;
        }
    }

    private static string BuildMoodleGetUrl(string baseUrl, string token, string wsFunction, IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new StringBuilder(baseUrl.TrimEnd('/')).Append("/webservice/rest/server.php?");
        builder.Append("wstoken=").Append(Uri.EscapeDataString(token));
        builder.Append("&wsfunction=").Append(Uri.EscapeDataString(wsFunction));
        builder.Append("&moodlewsrestformat=json");

        foreach (var pair in parameters)
        {
            builder.Append('&')
                .Append(Uri.EscapeDataString(pair.Key))
                .Append('=')
                .Append(Uri.EscapeDataString(pair.Value));
        }

        return builder.ToString();
    }

    private static int ParseMoodleId(string value, string parameterName)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

    private static string ToIdString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static DateTimeOffset? ToDateTimeOffset(JsonElement value)
    {
        var seconds = ToInt64(value);
        return seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;
    }

    private static long? ToInt64(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static bool? ToBool(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var boolean) => boolean,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number != 0,
            _ => null
        };
    }

    private sealed class ParticipantDto
    {
        [JsonPropertyName("id")]
        public JsonElement Id { get; init; }

        [JsonPropertyName("fullname")]
        public string? FullName { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("suspended")]
        public JsonElement Suspended { get; init; }

        [JsonPropertyName("firstaccess")]
        public JsonElement FirstAccess { get; init; }

        [JsonPropertyName("lastaccess")]
        public JsonElement LastAccess { get; init; }

        [JsonPropertyName("lastcourseaccess")]
        public JsonElement LastCourseAccess { get; init; }

        [JsonPropertyName("roles")]
        public IReadOnlyList<RoleDto>? Roles { get; init; }

        [JsonPropertyName("groups")]
        public IReadOnlyList<GroupDto>? Groups { get; init; }
    }

    private sealed class RoleDto
    {
        [JsonPropertyName("roleid")]
        public JsonElement RoleId { get; init; }

        [JsonPropertyName("shortname")]
        public string? ShortName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed class GroupDto
    {
        [JsonPropertyName("id")]
        public JsonElement Id { get; init; }

        [JsonPropertyName("courseid")]
        public JsonElement CourseId { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("idnumber")]
        public string? IdNumber { get; init; }
    }

    private async Task<string> ResolveReadTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.AllowServiceTokenForReadOnlyQueries && !string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            return _options.ServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
    }
}
