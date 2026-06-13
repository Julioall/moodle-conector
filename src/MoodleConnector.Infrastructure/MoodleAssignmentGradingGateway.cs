using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentGradingGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleAssignmentGradingGateway
{
    private const string MoodleFunction = "mod_assign_save_grade";
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<AssignmentGradeWriteResult> SaveGradeAsync(
        string userExternalId,
        AssignmentGradeWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para escritas Moodle reais.");
        }

        if (string.IsNullOrWhiteSpace(userExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", nameof(userExternalId));
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        if (!credentials.CanWrite)
        {
            throw new InvalidOperationException("A conexao Moodle atual nao permite escrita.");
        }

        var token = await ResolveWriteTokenAsync(cancellationToken);
        var endpoint = $"{credentials.BaseUrl.TrimEnd('/')}/webservice/rest/server.php";
        using var content = new FormUrlEncodedContent(BuildParameters(token, request));
        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfMoodleReturnedError(payload);

        return new AssignmentGradeWriteResult(
            Success: true,
            MoodleFunction,
            MoodleStatus: "ok");
    }

    private async Task<string> ResolveWriteTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.WriteServiceToken))
        {
            return _options.WriteServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
    }

    private static Dictionary<string, string> BuildParameters(
        string token,
        AssignmentGradeWriteRequest request)
    {
        return new Dictionary<string, string>
        {
            ["wstoken"] = token,
            ["wsfunction"] = MoodleFunction,
            ["moodlewsrestformat"] = "json",
            ["assignmentid"] = ParseMoodleId(request.AssignmentId, "assignmentId").ToString(CultureInfo.InvariantCulture),
            ["userid"] = ParseMoodleId(request.StudentId, "studentId").ToString(CultureInfo.InvariantCulture),
            ["grade"] = request.Grade.ToString(CultureInfo.InvariantCulture),
            ["attemptnumber"] = request.AttemptNumber.ToString(CultureInfo.InvariantCulture),
            ["addattempt"] = ToMoodleBool(request.AddAttempt),
            ["workflowstate"] = string.IsNullOrWhiteSpace(request.WorkflowState) ? "graded" : request.WorkflowState,
            ["applytoall"] = ToMoodleBool(request.ApplyToAll),
            ["plugindata[assignfeedbackcomments_editor][text]"] = request.FeedbackText,
            ["plugindata[assignfeedbackcomments_editor][format]"] = "1",
            ["plugindata[files_filemanager]"] = "0"
        };
    }

    private static int ParseMoodleId(string value, string parameterName)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

    private static string ToMoodleBool(bool value) => value ? "1" : "0";

    private static void ThrowIfMoodleReturnedError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            string.Equals(payload.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var root = document.RootElement;
        if (!root.TryGetProperty("exception", out var exceptionElement))
        {
            return;
        }

        var errorCode = root.TryGetProperty("errorcode", out var errorCodeElement)
            ? errorCodeElement.GetString()
            : exceptionElement.GetString();
        throw new InvalidOperationException($"O Moodle rejeitou a escrita de nota: {errorCode ?? "erro_desconhecido"}.");
    }
}
