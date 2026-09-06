using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentSettingsGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient,
    IMemoryCache memoryCache) : IMoodleAssignmentSettingsGateway
{
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
        string userExternalId,
        string courseId,
        string assignmentId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            // The local stub does not model the assignment grading scale. Do not
            // manufacture a numeric maximum: callers must keep numeric grading
            // blocked until a real, verifiable Moodle scale is available.
            return new AssignmentSettingsSummary(assignmentId, 0m, Name: null, IsGradable: null);
        }

        var normalizedCourseId = ParseMoodleId(courseId, nameof(courseId));
        var normalizedAssignmentId = ParseMoodleId(assignmentId, nameof(assignmentId));
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var cachePrefix = BuildCachePrefix(credentials, userExternalId);

        var cacheKey = $"{cachePrefix}:assignment:{normalizedCourseId}:{normalizedAssignmentId}";
        if (memoryCache.TryGetValue(cacheKey, out AssignmentSettingsSummary? cached) && cached is not null)
        {
            return cached;
        }

        var parameters = new Dictionary<string, string>
        {
            ["courseids[0]"] = normalizedCourseId.ToString(CultureInfo.InvariantCulture)
        };

        var payload = await restClient.CallAsync(
            credentials,
            "mod_assign_get_assignments",
            parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
            cancellationToken);
        var settings = ParseSettings(payload.GetRawText(), normalizedAssignmentId);

        if (settings is not null)
        {
            memoryCache.Set(cacheKey, settings, TimeSpan.FromMinutes(15));
        }

        return settings;
    }

    public async Task<IReadOnlyDictionary<string, AssignmentSettingsSummary>> GetCourseAssignmentSettingsAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            return new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal);
        }

        var normalizedCourseId = ParseMoodleId(courseId, nameof(courseId));
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var cachePrefix = BuildCachePrefix(credentials, userExternalId);
        var cacheKey = $"{cachePrefix}:course:{normalizedCourseId}";
        if (memoryCache.TryGetValue(
                cacheKey,
                out IReadOnlyDictionary<string, AssignmentSettingsSummary>? cached) && cached is not null)
        {
            return cached;
        }

        var parameters = new Dictionary<string, string>
        {
            ["courseids[0]"] = normalizedCourseId.ToString(CultureInfo.InvariantCulture)
        };

        var payload = await restClient.CallAsync(
            credentials,
            "mod_assign_get_assignments",
            parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
            cancellationToken);
        var settingsById = ParseAllSettings(payload.GetRawText());

        memoryCache.Set(cacheKey, settingsById, TimeSpan.FromMinutes(15));
        foreach (var pair in settingsById)
        {
            memoryCache.Set(
                $"{cachePrefix}:assignment:{normalizedCourseId}:{pair.Key}",
                pair.Value,
                TimeSpan.FromMinutes(15));
        }

        return settingsById;
    }

    private static string BuildCachePrefix(
        MoodleConnectorCredentials credentials,
        string userExternalId)
    {
        // Assignment visibility/settings can vary by authenticated Moodle
        // connection and actor. Keep every cache namespace tenant-safe; a
        // numeric course/assignment id is not globally unique across sites.
        static string Part(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "_" : value.Trim().Replace(":", "%3A", StringComparison.Ordinal);

        return $"moodle_assign_settings:{Part(credentials.ClientId)}:{Part(credentials.ConnectionId)}:{Part(credentials.Alias)}:{Part(userExternalId)}";
    }

    private static AssignmentSettingsSummary? ParseSettings(string payload, long assignmentId)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("courses", out var courses) ||
            courses.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var course in courses.EnumerateArray())
        {
            if (!course.TryGetProperty("assignments", out var assignments) || assignments.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var assignment in assignments.EnumerateArray())
            {
                if (!assignment.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                var currentId = idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt64() :
                                long.TryParse(idElement.GetString(), out var idParsed) ? idParsed : -1;

                // Also check cmid just in case the provided assignmentId was the module id instead of instance id
                var cmid = -1L;
                if (assignment.TryGetProperty("cmid", out var cmidElement))
                {
                    cmid = cmidElement.ValueKind == JsonValueKind.Number ? cmidElement.GetInt64() :
                           long.TryParse(cmidElement.GetString(), out var cmidParsed) ? cmidParsed : -1;
                }

                if (currentId == assignmentId || cmid == assignmentId)
                {
                    // Parse assignment name
                    string? assignmentName = null;
                    if (assignment.TryGetProperty("name", out var nameElement) &&
                        nameElement.ValueKind == JsonValueKind.String)
                    {
                        assignmentName = nameElement.GetString();
                    }
                    var assignmentDescription = ReadString(assignment, "intro");

                    if (assignment.TryGetProperty("grade", out var gradeElement))
                    {
                        var hasGrade = TryReadDecimal(gradeElement, out var grade);
                        
                        // Moodle retorna grade negativo para indicar que a atividade usa escala
                        // (o valor absoluto é o ID da escala). Nesse caso, MaxGrade fica 0
                        // pois não temos conversão de escala implementada.
                        var effectiveMaxGrade = hasGrade && grade > 0 ? grade : 0m;

                        return new AssignmentSettingsSummary(
                            currentId.ToString(CultureInfo.InvariantCulture),
                            effectiveMaxGrade,
                            assignmentName,
                            IsGradable: hasGrade ? grade != 0m : null,
                            Description: assignmentDescription);
                    }

                    // No grade property but found the assignment — return with name only
                    if (assignmentName is not null)
                    {
                        return new AssignmentSettingsSummary(
                            currentId.ToString(CultureInfo.InvariantCulture),
                            MaxGrade: 0m,
                            assignmentName,
                            IsGradable: null,
                            Description: assignmentDescription);
                    }
                }
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, AssignmentSettingsSummary> ParseAllSettings(string payload)
    {
        var result = new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return result;
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("courses", out var courses) ||
            courses.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var course in courses.EnumerateArray())
        {
            if (!course.TryGetProperty("assignments", out var assignments) ||
                assignments.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var assignment in assignments.EnumerateArray())
            {
                var assignmentId = ReadId(assignment, "id");
                if (assignmentId <= 0)
                {
                    continue;
                }

                var cmid = ReadId(assignment, "cmid");
                var name = ReadString(assignment, "name");
                var description = ReadString(assignment, "intro");
                decimal grade = 0m;
                var hasGrade = assignment.TryGetProperty("grade", out var gradeElement) &&
                    TryReadDecimal(gradeElement, out grade);
                var settings = new AssignmentSettingsSummary(
                    assignmentId.ToString(CultureInfo.InvariantCulture),
                    grade > 0 ? grade : 0m,
                    name,
                    IsGradable: hasGrade ? grade != 0m : null,
                    Description: description);

                result[assignmentId.ToString(CultureInfo.InvariantCulture)] = settings;
                if (cmid > 0)
                {
                    result[cmid.ToString(CultureInfo.InvariantCulture)] = settings;
                }
            }
        }

        return result;
    }

    private static long ReadId(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return -1;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => -1
        };
    }

    private static bool TryReadDecimal(JsonElement value, out decimal result)
    {
        result = 0m;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            result = number;
            return true;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long ParseMoodleId(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }
}
