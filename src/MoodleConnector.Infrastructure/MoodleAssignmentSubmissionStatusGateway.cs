using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentSubmissionStatusGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleAssignmentSubmissionStatusGateway
{
    private const string MoodleFunction = "mod_assign_get_submission_status";
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<AssignmentSubmissionAttemptStatus?> GetSubmissionStatusAsync(
        string userExternalId,
        string assignmentId,
        string studentId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(userExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", nameof(userExternalId));
        }

        var assignmentIdNumber = ParseMoodleId(assignmentId, nameof(assignmentId));
        var studentIdNumber = ParseMoodleId(studentId, nameof(studentId));
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var payload = await restClient.CallAsync(credentials, MoodleFunction, new Dictionary<string, object?>
        {
            ["assignid"] = assignmentIdNumber.ToString(CultureInfo.InvariantCulture),
            ["userid"] = studentIdNumber.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

        return ParseStatus(payload.GetRawText(), assignmentIdNumber, studentIdNumber);
    }

    private static AssignmentSubmissionAttemptStatus? ParseStatus(
        string payload,
        long assignmentId,
        long studentId)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            string.Equals(payload.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var root = document.RootElement;
        var lastAttempt = root.TryGetProperty("lastattempt", out var lastAttemptElement) &&
            lastAttemptElement.ValueKind == JsonValueKind.Object
                ? lastAttemptElement
                : default;
        var submission = lastAttempt.ValueKind == JsonValueKind.Object &&
            lastAttempt.TryGetProperty("submission", out var submissionElement) &&
            submissionElement.ValueKind == JsonValueKind.Object
                ? submissionElement
                : default;

        var attemptNumber = ReadNullableIntProperty(submission, "attemptnumber") ??
            ReadNullableIntProperty(lastAttempt, "attemptnumber") ??
            ReadNullableIntProperty(root, "attemptnumber");
        var submissionStatus = ReadStringProperty(submission, "status") ??
            ReadStringProperty(lastAttempt, "submissionstatus") ??
            ReadStringProperty(root, "status");

        return new AssignmentSubmissionAttemptStatus(
            assignmentId.ToString(CultureInfo.InvariantCulture),
            studentId.ToString(CultureInfo.InvariantCulture),
            attemptNumber,
            string.IsNullOrWhiteSpace(submissionStatus) ? null : submissionStatus,
            HasFeedback: HasExistingFeedback(root));
    }

    private static int? ReadNullableIntProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool HasExistingFeedback(JsonElement root)
    {
        if (!root.TryGetProperty("feedback", out var feedback) ||
            feedback.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!feedback.TryGetProperty("plugins", out var plugins) ||
            plugins.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var plugin in plugins.EnumerateArray())
        {
            if (plugin.TryGetProperty("editorfields", out var editorFields) &&
                editorFields.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in editorFields.EnumerateArray())
                {
                    if (field.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(text.GetString()))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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
