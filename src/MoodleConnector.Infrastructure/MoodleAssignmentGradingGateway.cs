using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentGradingGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleAssignmentGradingGateway
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

        await restClient.CallAsync(credentials, MoodleFunction, BuildParameters(request), allowServiceToken: false, cancellationToken);

        return new AssignmentGradeWriteResult(
            Success: true,
            MoodleFunction,
            MoodleStatus: "ok");
    }

    private static Dictionary<string, object?> BuildParameters(AssignmentGradeWriteRequest request)
    {
        return new Dictionary<string, object?>
        {
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

}
