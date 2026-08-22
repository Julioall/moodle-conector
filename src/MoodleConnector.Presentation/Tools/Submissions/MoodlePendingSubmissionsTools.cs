using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
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
            if (snapshotContext is not null)
            {
                try
                {
                    var scope = await snapshotContext.TryResolveAsync(moodleAlias, cancellationToken);
                    if (scope is not null)
                    {
                        resolvedCourseId = await snapshotContext.ResolveCourseIdAsync(scope, courseId, cancellationToken);
                        var activities = await snapshotContext.GetActivitiesAsync(scope, resolvedCourseId, cancellationToken);
                        var students = await snapshotContext.GetStudentsAsync(scope, resolvedCourseId, cancellationToken);
                        var submissions = await snapshotContext.GetSubmissionsAsync(scope, resolvedCourseId, cancellationToken);
                        if (activities?.IsComplete == true) prefetchedContents = activities.Data;
                        if (students?.IsComplete == true) prefetchedParticipants = students.Data;
                        if (submissions?.Data is not null) prefetchedSubmissions = submissions.Data;

                        if (submissions is not null)
                        {
                            var refreshQueued = submissions.IsStale && await snapshotContext.QueueAsync(
                                scope,
                                moodleUserId.Value.ToString(),
                                MoodleSnapshotDatasets.Submissions,
                                resolvedCourseId,
                                priority: 10,
                                force: submissions.IsStale,
                                cancellationToken);
                            freshness = new ToolFreshness(
                                "snapshot",
                                submissions.UpdatedAt,
                                Math.Max(0, (long)(DateTimeOffset.UtcNow - submissions.UpdatedAt).TotalSeconds),
                                submissions.IsStale,
                                refreshQueued,
                                submissions.IsComplete,
                                submissions.RecordCount);
                        }
                        else
                        {
                            var refreshQueued = await snapshotContext.QueueAsync(
                                scope,
                                moodleUserId.Value.ToString(),
                                MoodleSnapshotDatasets.Submissions,
                                resolvedCourseId,
                                priority: 10,
                                cancellationToken: cancellationToken);
                            freshness = new ToolFreshness("live", null, null, false, refreshQueued, false, 0);
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
                    PrefetchedSubmissions: prefetchedSubmissions),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<GetStudentsWithPendingSubmissionsResult>("Não foi possível listar os alunos com atividades pendentes neste momento.");
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
