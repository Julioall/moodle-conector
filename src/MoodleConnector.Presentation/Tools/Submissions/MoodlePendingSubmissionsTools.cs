using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Submissions.Queries;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Presentation.Tools.Submissions;

[McpServerToolType]
public sealed class MoodlePendingSubmissionsTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleSnapshotToolContext? snapshotContext = null)
{
    [McpServerTool(
        Name = "list_students_with_pending_submissions",
        Title = "List Students With Pending Submissions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetStudentsWithPendingSubmissionsResult>))]
    [Description("Lista estudantes que possuem atividades (SAs) pendentes de entrega. Consolida por estudante mostrando quais atividades estão pendentes. Use DueDaysAhead=0 para ver todas sem entrega, ou um número para filtrar pelo prazo (ex: 7 = próximos 7 dias ou vencidas).")]
    public Task<CallToolResult> ListarAlunosPendentesAtividadeAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Filtrar por prazo: 0 = todas as pendentes, N = apenas atividades com prazo nos próximos N dias (ou já vencidas). Padrão: 0.")]
        int dueDaysAhead = 0,
        [Description("Máximo de estudantes para analisar. Padrão: 100.")]
        int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetPendingSubmissionsCoreAsync(courseId, dueDaysAhead, maxStudentsToAnalyze, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetPendingSubmissionsCoreAsync(
        string courseId,
        int dueDaysAhead,
        int maxStudentsToAnalyze,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<GetStudentsWithPendingSubmissionsResult>("Informe um identificador de curso válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<GetStudentsWithPendingSubmissionsResult>("Usuário não autenticado.");

        GetStudentsWithPendingSubmissionsResult data;
        ToolFreshness? freshness = null;
        try
        {
            var resolvedCourseId = courseId;
            CourseContentsSummary? prefetchedContents = null;
            CourseParticipantsPage? prefetchedParticipants = null;
            CourseAssignmentSubmissionsSnapshot? prefetchedSubmissions = null;
            CourseGradebookSnapshot? prefetchedGradebook = null;
            if (snapshotContext is not null)
            {
                try
                {
                    var courseRead = await snapshotContext.ReadAsync(
                        new CourseReadSnapshotRequest(
                            courseId,
                            moodleAlias,
                            moodleUserId.Value.ToString(),
                            CourseReadSnapshotRequirements.Activities |
                            CourseReadSnapshotRequirements.Students |
                            CourseReadSnapshotRequirements.Submissions |
                            CourseReadSnapshotRequirements.Gradebook),
                        cancellationToken);
                    if (courseRead is not null)
                    {
                        resolvedCourseId = courseRead.CourseId;
                        var activities = courseRead.Activities;
                        var students = courseRead.Students;
                        var submissions = courseRead.Submissions;
                        var gradebook = courseRead.Gradebook;
                        if (activities?.IsComplete == true) prefetchedContents = activities.Data;
                        if (students?.IsComplete == true) prefetchedParticipants = students.Data;
                        if (submissions?.Data is not null) prefetchedSubmissions = submissions.Data;
                        if (gradebook?.Data is not null) prefetchedGradebook = gradebook.Data;

                        if (submissions is not null || gradebook is not null)
                        {
                            var updatedAt = new[] { submissions?.UpdatedAt, gradebook?.UpdatedAt }
                                .Where(value => value.HasValue)
                                .Select(value => value!.Value)
                                .OrderByDescending(value => value)
                                .FirstOrDefault();
                            freshness = new ToolFreshness(
                                "snapshot",
                                updatedAt == default ? null : updatedAt,
                                updatedAt == default ? null : Math.Max(0, (long)(DateTimeOffset.UtcNow - updatedAt).TotalSeconds),
                                courseRead.Metadata.StaleDatasets.Count > 0,
                                courseRead.Metadata.RefreshQueued,
                                courseRead.Metadata.IsComplete,
                                (submissions?.RecordCount ?? 0) + (gradebook?.RecordCount ?? 0));
                        }
                    }
                }
                catch
                {
                    // Snapshot lookup is an optimization. Preserve the live
                    // query path when the local account or snapshot store is
                    // unavailable.
                }
            }

            data = await mediator.Send(
                new GetStudentsWithPendingSubmissionsQuery(
                    resolvedCourseId,
                    dueDaysAhead,
                    maxStudentsToAnalyze,
                    IncludeAwaitingGrading: true,
                    PrefetchedContents: prefetchedContents,
                    PrefetchedParticipants: prefetchedParticipants,
                    PrefetchedSubmissions: prefetchedSubmissions,
                    PrefetchedGradebook: prefetchedGradebook),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException exception)
        {
            return ToolResultHelper.Error<GetStudentsWithPendingSubmissionsResult>(exception);
        }
        catch (Exception exception)
        {
            return ToolResultHelper.Error<GetStudentsWithPendingSubmissionsResult>(exception);
        }

        var response = new ToolResponse<GetStudentsWithPendingSubmissionsResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow, Freshness: freshness);
        var filter = dueDaysAhead > 0 ? $"nos próximos {dueDaysAhead} dias ou vencidas" : "sem filtro de prazo";
        var narration = $"Pendências de atividade — curso {courseId} ({filter}): {data.TotalStudentsAnalyzed} estudante(s) analisado(s). " +
                        $"{data.Students.Count} com pelo menos uma SA pendente.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }
}
