using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentSubmissionStatusGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleAssignmentSubmissionStatusGateway
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
        var token = await ResolveTokenAsync(cancellationToken);
        var endpoint = $"{credentials.BaseUrl.TrimEnd('/')}/webservice/rest/server.php";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["wstoken"] = token,
            ["wsfunction"] = MoodleFunction,
            ["moodlewsrestformat"] = "json",
            ["assignid"] = assignmentIdNumber.ToString(CultureInfo.InvariantCulture),
            ["userid"] = studentIdNumber.ToString(CultureInfo.InvariantCulture)
        });
        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfMoodleReturnedError(payload);

        return ParseStatus(payload, assignmentIdNumber, studentIdNumber);
    }

    private async Task<string> ResolveTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.WriteServiceToken))
        {
            return _options.WriteServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
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
            string.IsNullOrWhiteSpace(submissionStatus) ? null : submissionStatus);
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

    private static long ParseMoodleId(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

    private static void ThrowIfMoodleReturnedError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            string.Equals(payload.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("exception", out var exceptionElement))
        {
            return;
        }

        var errorCode = document.RootElement.TryGetProperty("errorcode", out var errorCodeElement)
            ? errorCodeElement.GetString()
            : exceptionElement.GetString();
        throw new InvalidOperationException($"O Moodle rejeitou a leitura do status da submissao: {errorCode ?? "erro_desconhecido"}.");
    }
}
