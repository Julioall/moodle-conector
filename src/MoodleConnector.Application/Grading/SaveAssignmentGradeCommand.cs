using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Application.Grading;

public sealed record SaveAssignmentGradeCommand(
    string UserExternalId,
    string AssignmentId,
    string StudentId,
    decimal? Grade,
    string FeedbackText,
    int AttemptNumber,
    bool AddAttempt,
    bool ApplyToAll,
    string WorkflowState,
    string? CourseId = null) : IRequest<AssignmentGradeWriteResult>;

public sealed record AssignmentGradeWriteRequest(
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("grade")] decimal? Grade,
    [property: JsonPropertyName("feedbackText")] string FeedbackText,
    [property: JsonPropertyName("attemptNumber")] int AttemptNumber,
    [property: JsonPropertyName("addAttempt")] bool AddAttempt,
    [property: JsonPropertyName("applyToAll")] bool ApplyToAll,
    [property: JsonPropertyName("workflowState")] string WorkflowState);

public sealed record AssignmentGradeWriteResult(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("moodleFunction")] string MoodleFunction,
    [property: JsonPropertyName("moodleStatus")] string MoodleStatus);

public sealed class SaveAssignmentGradeCommandHandler(
    IMoodleAssignmentGradingGateway gateway,
    IOptions<AssignmentWriteFeatureOptions> features,
    IMoodleAssignmentSettingsGateway settingsGateway)
    : IRequestHandler<SaveAssignmentGradeCommand, AssignmentGradeWriteResult>
{
    public async Task<AssignmentGradeWriteResult> Handle(
        SaveAssignmentGradeCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", "userExternalId");
        }

        if (string.IsNullOrWhiteSpace(request.AssignmentId))
        {
            throw new ArgumentException("O identificador da tarefa e obrigatorio.", "assignmentId");
        }

        if (string.IsNullOrWhiteSpace(request.StudentId))
        {
            throw new ArgumentException("O identificador do estudante e obrigatorio.", "studentId");
        }

        if (request.Grade < 0)
        {
            throw new ArgumentOutOfRangeException("grade", "A nota nao pode ser negativa.");
        }

        if (request.Grade is not null && !features.Value.AssignmentGradeWriteEnabled)
        {
            throw new InvalidOperationException("A escrita de notas em tarefas esta desabilitada por feature flag.");
        }

        if (!features.Value.AssignmentFeedbackWriteEnabled &&
            !string.IsNullOrWhiteSpace(request.FeedbackText))
        {
            throw new InvalidOperationException("A escrita de feedback em tarefas esta desabilitada por feature flag.");
        }

        if (request.Grade is not null)
        {
            if (string.IsNullOrWhiteSpace(request.CourseId))
            {
                throw new InvalidOperationException("O curso da tarefa nao foi informado; escala maxima nao pode ser confirmada.");
            }

            AssignmentSettingsSummary? settings;
            try
            {
                settings = await settingsGateway.GetAssignmentSettingsAsync(
                    request.UserExternalId.Trim(),
                    request.CourseId.Trim(),
                    request.AssignmentId.Trim(),
                    cancellationToken);
            }
            catch
            {
                throw new InvalidOperationException("A escala maxima da tarefa nao pode ser confirmada; lancamento bloqueado.");
            }

            if (settings?.MaxGrade is not > 0)
            {
                throw new InvalidOperationException("A escala maxima da tarefa nao pode ser confirmada; lancamento bloqueado.");
            }

            if (request.Grade > settings.MaxGrade)
            {
                throw new ArgumentOutOfRangeException(
                    "grade",
                    $"A nota deve estar entre 0 e {settings.MaxGrade.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            }
        }

        var writeRequest = new AssignmentGradeWriteRequest(
            request.AssignmentId.Trim(),
            request.StudentId.Trim(),
            request.Grade,
            request.FeedbackText?.Trim() ?? string.Empty,
            request.AttemptNumber,
            request.AddAttempt,
            request.ApplyToAll,
            string.IsNullOrWhiteSpace(request.WorkflowState) ? "graded" : request.WorkflowState.Trim());

        return await gateway.SaveGradeAsync(
            request.UserExternalId.Trim(),
            writeRequest,
            cancellationToken);
    }
}
