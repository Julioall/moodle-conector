using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleParticipantsGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleParticipantsGateway
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
        // The all-enrolments response on some Moodle installations omits the
        // per-course suspended flag. Read the two authoritative enrolment
        // populations instead so `status=todos` never degrades known active
        // or suspended records to `unknown`.
        if (statusFilter == ParticipantStatusFilter.All)
        {
            return await GetParticipantsAcrossEnrollmentStatusesAsync(
                userExternalId,
                courseId,
                page,
                pageSize,
                studentsOnly,
                includeEmail,
                groupId,
                cancellationToken);
        }

        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var normalizedCourseId = ParseMoodleId(courseId, "courseId");
        var normalizedGroupId = string.IsNullOrWhiteSpace(groupId)
            ? (int?)null
            : ParseMoodleId(groupId, "groupId");
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);

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
                credentials,
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

            foreach (var participant in moodleParticipants.Select(dto => ToParticipant(dto, includeEmail, statusFilter)))
            {
                evaluatedCount++;
                hasEmptyRoles |= participant.Roles.Count == 0;
                hasEmptyGroups |= participant.Groups.Count == 0;

                var classification = ParticipantClassification.Classify(participant);
                if (studentsOnly && classification == ParticipantClassificationKind.KnownStaff)
                {
                    excludedKnownStaffCount++;
                    continue;
                }

                // Moodle integrations occasionally return a role-less record
                // with no display name (for example, an inaccessible staff
                // account or an anonymised service user).  It cannot be
                // safely attributed to a student, so do not leak it into
                // student-facing reports and submission joins.  Keep the
                // named role-less fallback for installations that omit roles
                // from otherwise valid student records.
                if (studentsOnly &&
                    classification == ParticipantClassificationKind.UncertainFallback &&
                    string.IsNullOrWhiteSpace(participant.FullName))
                {
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

    private async Task<CourseParticipantsPage> GetParticipantsAcrossEnrollmentStatusesAsync(
        string userExternalId,
        string courseId,
        int page,
        int pageSize,
        bool studentsOnly,
        bool includeEmail,
        string? groupId,
        CancellationToken cancellationToken)
    {
        var active = await ReadAllPagesForStatusAsync(
            userExternalId, courseId, ParticipantStatusFilter.Active, studentsOnly, includeEmail, groupId, cancellationToken);
        var suspended = await ReadAllPagesForStatusAsync(
            userExternalId, courseId, ParticipantStatusFilter.Suspended, studentsOnly, includeEmail, groupId, cancellationToken);

        var allParticipants = active.Participants
            .Concat(suspended.Participants)
            .GroupBy(participant => participant.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (normalizedPage - 1) * normalizedPageSize;
        var diagnostics = MergeDiagnostics(active.Diagnostics, suspended.Diagnostics);

        return new CourseParticipantsPage(
            active.CourseId,
            page,
            normalizedPageSize,
            ParticipantStatusFilter.All,
            studentsOnly,
            includeEmail,
            skip + normalizedPageSize < allParticipants.Length,
            allParticipants.Skip(skip).Take(normalizedPageSize).ToArray(),
            diagnostics);
    }

    private async Task<(string CourseId, IReadOnlyList<CourseParticipantSummary> Participants, ParticipantClassificationDiagnostics Diagnostics)> ReadAllPagesForStatusAsync(
        string userExternalId,
        string courseId,
        ParticipantStatusFilter statusFilter,
        bool studentsOnly,
        bool includeEmail,
        string? groupId,
        CancellationToken cancellationToken)
    {
        var participants = new List<CourseParticipantSummary>();
        var diagnostics = new List<ParticipantClassificationDiagnostics>();
        var page = 1;
        string? normalizedCourseId = null;

        while (true)
        {
            var result = await GetCourseParticipantsAsync(
                userExternalId,
                courseId,
                statusFilter,
                page,
                ParticipantFetchBatchSize,
                studentsOnly,
                includeEmail,
                groupId,
                cancellationToken);
            normalizedCourseId ??= result.CourseId;
            participants.AddRange(result.Participants);
            if (result.ClassificationDiagnostics is not null)
            {
                diagnostics.Add(result.ClassificationDiagnostics);
            }

            if (!result.HasMore || result.Participants.Count == 0)
            {
                break;
            }

            page++;
        }

        return (normalizedCourseId ?? courseId, participants, MergeDiagnostics(diagnostics));
    }

    private static ParticipantClassificationDiagnostics MergeDiagnostics(
        params ParticipantClassificationDiagnostics[] diagnostics) =>
        MergeDiagnostics((IEnumerable<ParticipantClassificationDiagnostics>)diagnostics);

    private static ParticipantClassificationDiagnostics MergeDiagnostics(
        IEnumerable<ParticipantClassificationDiagnostics> source)
    {
        var diagnostics = source.ToArray();
        var evaluated = diagnostics.Sum(item => item.EvaluatedCount);
        var roleBased = diagnostics.Sum(item => item.IncludedByStudentRoleCount);
        var fallback = diagnostics.Sum(item => item.IncludedByFallbackCount);
        return new ParticipantClassificationDiagnostics(
            evaluated,
            roleBased,
            fallback,
            diagnostics.Sum(item => item.ExcludedKnownStaffCount),
            diagnostics.Any(item => item.HasEmptyRoles),
            diagnostics.Any(item => item.HasEmptyGroups),
            ResolveClassificationMode(evaluated, roleBased, fallback));
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
        try
        {
            var payload = await restClient.CallAsync(
                credentials,
                "core_group_get_course_groups",
                new Dictionary<string, string>
                {
                    ["courseid"] = normalizedCourseId.ToString(CultureInfo.InvariantCulture)
                }.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
                cancellationToken);

            return MapGroups(
                JsonSerializer.Deserialize<List<GroupDto>>(payload.GetRawText()) ?? [],
                normalizedCourseId.ToString(CultureInfo.InvariantCulture));
        }
        catch (MoodleApiException exception) when (IsPermissionDenied(exception))
        {
            // Some Moodle roles can see groups embedded in enrolled users but
            // are not allowed to call core_group_get_course_groups directly.
            return await GetCourseGroupsFromParticipantsAsync(
                credentials,
                normalizedCourseId,
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ParticipantDto>> GetParticipantsBatchAsync(
        MoodleConnectorCredentials credentials,
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

        // Enrolment suspension is course-specific. The `suspended` field in a
        // user record may be absent (or represent account suspension), so the
        // Moodle enrolment filter is the authoritative source for this query.
        if (statusFilter == ParticipantStatusFilter.Active)
        {
            options.Add(("onlyactive", "1"));
        }
        else if (statusFilter == ParticipantStatusFilter.Suspended)
        {
            options.Add(("onlysuspended", "1"));
        }

        if (groupId is not null)
        {
            options.Add(("groupid", groupId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        AddMoodleOptions(parameters, options);

        var payload = await restClient.CallAsync(
            credentials,
            "core_enrol_get_enrolled_users",
            parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
            cancellationToken);

        return JsonSerializer.Deserialize<List<ParticipantDto>>(payload.GetRawText()) ?? [];
    }

    private async Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsFromParticipantsAsync(
        MoodleConnectorCredentials credentials,
        int courseId,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, CourseGroupSummary>(StringComparer.Ordinal);
        var offset = 0;

        while (true)
        {
            var participants = await GetParticipantsBatchAsync(
                credentials,
                courseId,
                ParticipantStatusFilter.All,
                includeEmail: false,
                groupId: null,
                offset,
                ParticipantFetchBatchSize,
                cancellationToken);

            if (participants.Count == 0)
            {
                break;
            }

            foreach (var participant in participants)
            {
                foreach (var group in participant.Groups ?? [])
                {
                    var groupId = ToIdString(group.Id);
                    if (string.IsNullOrWhiteSpace(groupId))
                    {
                        continue;
                    }

                    groups.TryAdd(
                        groupId,
                        new CourseGroupSummary(
                            groupId,
                            courseId.ToString(CultureInfo.InvariantCulture),
                            group.Name ?? string.Empty,
                            string.IsNullOrWhiteSpace(group.IdNumber) ? null : group.IdNumber));
                }
            }

            offset += participants.Count;
            if (participants.Count < ParticipantFetchBatchSize)
            {
                break;
            }
        }

        return groups.Values
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.GroupId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<CourseGroupSummary> MapGroups(
        IReadOnlyList<GroupDto> groups,
        string defaultCourseId) => groups
        .Select(group => new CourseGroupSummary(
            ToIdString(group.Id),
            string.IsNullOrWhiteSpace(ToIdString(group.CourseId))
                ? defaultCourseId
                : ToIdString(group.CourseId),
            group.Name ?? string.Empty,
            string.IsNullOrWhiteSpace(group.IdNumber) ? null : group.IdNumber))
        .Where(group => !string.IsNullOrWhiteSpace(group.GroupId))
        .ToArray();

    private static bool IsPermissionDenied(MoodleApiException exception) =>
        MoodleErrorContract.NormalizeCode(exception.ErrorCode) == MoodleErrorContract.PermissionDenied ||
        MoodleErrorContract.NormalizeCode(exception.RemoteErrorCode) == MoodleErrorContract.PermissionDenied;

    private static CourseParticipantSummary ToParticipant(
        ParticipantDto dto,
        bool includeEmail,
        ParticipantStatusFilter requestedStatus)
    {
        var reportedSuspended = ToBool(dto.Suspended);
        // onlyactive/onlysuspended are course-enrolment filters and therefore
        // are authoritative even when Moodle omits the `suspended` field.
        var suspended = requestedStatus switch
        {
            ParticipantStatusFilter.Active => reportedSuspended ?? false,
            ParticipantStatusFilter.Suspended => reportedSuspended ?? true,
            _ => reportedSuspended
        };

        return new CourseParticipantSummary(
            ToIdString(dto.Id),
            dto.FullName ?? string.Empty,
            includeEmail && !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : null,
            suspended,
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
                .ToArray(),
            suspended switch
            {
                true => "suspended",
                false => "active",
                _ => "unknown"
            });
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

}
