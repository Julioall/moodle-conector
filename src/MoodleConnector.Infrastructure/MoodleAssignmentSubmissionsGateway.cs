using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentSubmissionsGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleAssignmentSubmissionsGateway
{
    private const int MaxAssignmentIdsPerRequest = 50;
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<IReadOnlyList<AssignmentSubmissionsBatch>> GetAssignmentSubmissionsBatchAsync(
        string userExternalId,
        IReadOnlyCollection<string> assignmentIds,
        string? status,
        DateTimeOffset? since,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var normalizedAssignmentIds = assignmentIds
            .Select(id => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value.ToString(CultureInfo.InvariantCulture)
                : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedAssignmentIds.Length == 0)
        {
            return [];
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var result = new List<AssignmentSubmissionsBatch>(normalizedAssignmentIds.Length);

        foreach (var chunk in normalizedAssignmentIds.Chunk(MaxAssignmentIdsPerRequest))
        {
            var parameters = chunk
                .Select((assignmentId, index) => new KeyValuePair<string, object?>(
                    $"assignmentids[{index}]",
                    assignmentId))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            AddOptionalParameters(parameters, status, since, before);

            var payload = await restClient.CallAsync(
                credentials,
                "mod_assign_get_submissions",
                parameters,
                cancellationToken);
            var submissions = JsonSerializer.Deserialize<GetSubmissionsResponseDto>(payload.GetRawText());

            foreach (var assignment in submissions?.Assignments ?? [])
            {
                result.Add(new AssignmentSubmissionsBatch(
                    ToIdString(assignment.AssignmentId),
                    (assignment.Submissions ?? []).Select(ToRecord).ToArray()));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<AssignmentSubmissionRecord>> GetAssignmentSubmissionsAsync(
        string userExternalId,
        string assignmentId,
        string? status,
        DateTimeOffset? since,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var normalizedAssignmentId = ParseMoodleId(assignmentId, "assignmentId");
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);

        var parameters = new Dictionary<string, object?>
        {
            ["assignmentids[0]"] = normalizedAssignmentId.ToString(CultureInfo.InvariantCulture)
        };
        AddOptionalParameters(parameters, status, since, before);

        var payload = await restClient.CallAsync(
            credentials,
            "mod_assign_get_submissions",
            parameters,
            cancellationToken);

        var submissions = JsonSerializer.Deserialize<GetSubmissionsResponseDto>(payload.GetRawText());
        return (submissions?.Assignments ?? [])
            .SelectMany(assignment => assignment.Submissions ?? [])
            .Select(ToRecord)
            .ToArray();
    }

    private static void AddOptionalParameters(
        IDictionary<string, object?> parameters,
        string? status,
        DateTimeOffset? since,
        DateTimeOffset? before)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            parameters["status"] = status;
        }

        if (since is not null)
        {
            parameters["since"] = since.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        if (before is not null)
        {
            parameters["before"] = before.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }
    }

    private static AssignmentSubmissionRecord ToRecord(SubmissionDto dto)
    {
        var fileCount = 0;
        var hasOnlineText = false;
        var files = new List<AssignmentSubmissionFile>();
        foreach (var plugin in dto.Plugins ?? [])
        {
            foreach (var fileArea in plugin.FileAreas ?? [])
            {
                foreach (var file in fileArea.Files ?? [])
                {
                    fileCount++;
                    var submissionFile = ToSubmissionFile(file);
                    if (submissionFile is not null)
                    {
                        files.Add(submissionFile);
                    }
                }
            }

            hasOnlineText = hasOnlineText ||
                string.Equals(plugin.Type, "onlinetext", StringComparison.OrdinalIgnoreCase) &&
                (plugin.EditorFields?.Count ?? 0) > 0;
        }

        return new AssignmentSubmissionRecord(
            ToIdString(dto.Id),
            ToIdString(dto.UserId),
            string.IsNullOrWhiteSpace(dto.Status) ? "unknown" : dto.Status,
            string.IsNullOrWhiteSpace(dto.GradingStatus) ? null : dto.GradingStatus,
            ToDateTimeOffset(dto.TimeCreated),
            ToDateTimeOffset(dto.TimeModified),
            ToNullableInt(dto.AttemptNumber),
            fileCount,
            hasOnlineText,
            files);
    }

    private static AssignmentSubmissionFile? ToSubmissionFile(JsonElement file)
    {
        var fileUrl = GetString(file, "fileurl");
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return null;
        }

        var filename = GetString(file, "filename");
        if (string.IsNullOrWhiteSpace(filename))
        {
            filename = GetString(file, "filepath")?.Trim('/') ?? "submission-file";
        }

        return new AssignmentSubmissionFile(
            filename,
            GetString(file, "mimetype"),
            GetNullableLong(file, "filesize"),
            fileUrl);
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

    private static int? ToNullableInt(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static long? GetNullableLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static DateTimeOffset? ToDateTimeOffset(JsonElement value)
    {
        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0
        };

        return seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    private sealed class GetSubmissionsResponseDto
    {
        [JsonPropertyName("assignments")]
        public IReadOnlyList<AssignmentDto>? Assignments { get; init; }
    }

    private sealed class AssignmentDto
    {
        [JsonPropertyName("assignmentid")]
        public JsonElement AssignmentId { get; init; }

        [JsonPropertyName("submissions")]
        public IReadOnlyList<SubmissionDto>? Submissions { get; init; }
    }

    private sealed class SubmissionDto
    {
        [JsonPropertyName("id")]
        public JsonElement Id { get; init; }

        [JsonPropertyName("userid")]
        public JsonElement UserId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("gradingstatus")]
        public string? GradingStatus { get; init; }

        [JsonPropertyName("attemptnumber")]
        public JsonElement AttemptNumber { get; init; }

        [JsonPropertyName("timecreated")]
        public JsonElement TimeCreated { get; init; }

        [JsonPropertyName("timemodified")]
        public JsonElement TimeModified { get; init; }

        [JsonPropertyName("plugins")]
        public IReadOnlyList<SubmissionPluginDto>? Plugins { get; init; }
    }

    private sealed class SubmissionPluginDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("fileareas")]
        public IReadOnlyList<SubmissionFileAreaDto>? FileAreas { get; init; }

        [JsonPropertyName("editorfields")]
        public IReadOnlyList<JsonElement>? EditorFields { get; init; }
    }

    private sealed class SubmissionFileAreaDto
    {
        [JsonPropertyName("files")]
        public IReadOnlyList<JsonElement>? Files { get; init; }
    }

}
