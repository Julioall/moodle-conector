using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentSettingsGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider,
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
            return new AssignmentSettingsSummary(assignmentId, 100m, Name: null);
        }

        var normalizedCourseId = ParseMoodleId(courseId, nameof(courseId));
        var normalizedAssignmentId = ParseMoodleId(assignmentId, nameof(assignmentId));

        var cacheKey = $"moodle_assign_settings_{normalizedCourseId}_{normalizedAssignmentId}";
        if (memoryCache.TryGetValue(cacheKey, out AssignmentSettingsSummary? cached) && cached is not null)
        {
            return cached;
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);

        var parameters = new Dictionary<string, string>
        {
            ["courseids[0]"] = normalizedCourseId.ToString(CultureInfo.InvariantCulture)
        };

        var endpoint = BuildMoodleGetUrl(
            credentials.BaseUrl,
            token,
            "mod_assign_get_assignments",
            parameters);

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var settings = ParseSettings(payload, normalizedAssignmentId);

        if (settings is not null)
        {
            memoryCache.Set(cacheKey, settings, TimeSpan.FromMinutes(15));
        }

        return settings;
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

                    if (assignment.TryGetProperty("grade", out var gradeElement))
                    {
                        var grade = gradeElement.ValueKind == JsonValueKind.Number ? gradeElement.GetDecimal() :
                                    decimal.TryParse(gradeElement.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var gradeParsed) ? gradeParsed : 0m;
                        
                        // Moodle retorna grade negativo para indicar que a atividade usa escala
                        // (o valor absoluto é o ID da escala). Nesse caso, MaxGrade fica 0
                        // pois não temos conversão de escala implementada.
                        var effectiveMaxGrade = grade > 0 ? grade : 0m;

                        return new AssignmentSettingsSummary(
                            currentId.ToString(CultureInfo.InvariantCulture),
                            effectiveMaxGrade,
                            assignmentName);
                    }

                    // No grade property but found the assignment — return with name only
                    if (assignmentName is not null)
                    {
                        return new AssignmentSettingsSummary(
                            currentId.ToString(CultureInfo.InvariantCulture),
                            MaxGrade: 0m,
                            assignmentName);
                    }
                }
            }
        }

        return null;
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

    private static long ParseMoodleId(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }
}
