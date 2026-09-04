using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Presentation.Tools.Gradebook;

[McpServerToolType]
public sealed class MoodleStudentPerformanceTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleSnapshotToolContext? snapshotContext = null)
{
    // ── Desempenho por atividade ──────────────────────────────────────────────

    [McpServerTool(
        Name = "get_student_activity_grades",
        Title = "Get Student Activity Grades",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<StudentGradeItemsResult>))]
    [Description("Retorna os itens avaliativos (SAs) do boletim de um estudante com indicação de quais estão abaixo do conceito mínimo. Usado pelo tutor para identificar oportunidades de recuperação paralela.")]
    public Task<CallToolResult> ConsultarDesempenhoEstudantePorAtividadeAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Identificador do estudante (ID do Moodle).")]
        string studentId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")]
        decimal minGradePercent = 60m,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetGradeItemsCoreAsync(courseId, studentId, minGradePercent, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetGradeItemsCoreAsync(
        string courseId,
        string studentId,
        decimal minGradePercent,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<StudentGradeItemsResult>("Informe um identificador de curso válido.");

        if (string.IsNullOrWhiteSpace(studentId))
            return ToolResultHelper.Error<StudentGradeItemsResult>("Informe um identificador de estudante válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<StudentGradeItemsResult>("Usuário não autenticado.");

        var effectiveCourseId = courseId;
        CourseGradebookSnapshot? prefetchedGradebook = null;
        ToolFreshness? freshness = null;
        var snapshotContainsStudent = false;
        if (snapshotContext is not null)
        {
            try
            {
                var courseRead = await snapshotContext.ReadAsync(
                    new CourseReadSnapshotRequest(
                        courseId,
                        moodleAlias,
                        moodleUserId.Value.ToString(),
                        CourseReadSnapshotRequirements.Gradebook),
                    cancellationToken);
                if (courseRead is not null)
                {
                    effectiveCourseId = courseRead.CourseId;
                    prefetchedGradebook = courseRead.Gradebook?.Data;
                    snapshotContainsStudent = prefetchedGradebook?.TryGetForStudent(studentId, out _) == true;
                    if (snapshotContainsStudent && courseRead.Gradebook is not null)
                    {
                        freshness = new ToolFreshness(
                            "snapshot",
                            courseRead.Gradebook.UpdatedAt,
                            Math.Max(0, (long)(DateTimeOffset.UtcNow - courseRead.Gradebook.UpdatedAt).TotalSeconds),
                            courseRead.Gradebook.IsStale,
                            courseRead.Metadata.RefreshQueued,
                            courseRead.Gradebook.IsComplete && courseRead.Gradebook.Data.Coverage.IsComplete,
                            courseRead.Gradebook.RecordCount);
                    }
                    else
                    {
                        freshness = new ToolFreshness(
                            "live",
                            null,
                            null,
                            false,
                            courseRead.Metadata.RefreshQueued,
                            false,
                            0);
                    }
                }
            }
            catch
            {
                // Snapshot lookup is an optimization. Keep the live path.
            }
        }

        StudentGradeItemsResult data;
        try
        {
            data = await mediator.Send(
                new GetStudentGradeItemsQuery(
                    effectiveCourseId,
                    studentId,
                    minGradePercent,
                    prefetchedGradebook),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException exception)
        {
            return ToolResultHelper.Error<StudentGradeItemsResult>(exception);
        }
        catch (Exception exception)
        {
            return ToolResultHelper.Error<StudentGradeItemsResult>(exception);
        }

        var response = new ToolResponse<StudentGradeItemsResult>(
            "ok", data, [], AuditId: null, DateTimeOffset.UtcNow, Freshness: freshness);
        var narration = $"Desempenho do estudante {studentId} no curso {courseId}: {data.Items.Count} atividade(s) avaliativa(s). " +
                        $"{data.BelowMinimumItems.Count} abaixo do mínimo de {data.MinGradePercent}%.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    // ── Alunos abaixo do mínimo ───────────────────────────────────────────────

    [McpServerTool(
        Name = "list_students_below_min_grade",
        Title = "List Students Below Min Grade",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetStudentsBelowMinGradeResult>))]
    [Description("Lista todos os estudantes ativos com pelo menos uma SA/atividade abaixo do conceito mínimo. Retorna público-alvo sugerido para mensagem de recuperação paralela.")]
    public Task<CallToolResult> ListarAlunosAbaixoMinimoAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")]
        decimal minGradePercent = 60m,
        [Description("Máximo de estudantes para analisar. Padrão: 100.")]
        int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetStudentsBelowMinCoreAsync(courseId, minGradePercent, maxStudentsToAnalyze, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetStudentsBelowMinCoreAsync(
        string courseId,
        decimal minGradePercent,
        int maxStudentsToAnalyze,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<GetStudentsBelowMinGradeResult>("Informe um identificador de curso válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<GetStudentsBelowMinGradeResult>("Usuário não autenticado.");

        var effectiveCourseId = courseId;
        CourseGradebookSnapshot? prefetchedGradebook = null;
        CourseParticipantsPage? prefetchedParticipants = null;
        ToolFreshness? freshness = null;
        if (snapshotContext is not null)
        {
            try
            {
                var courseRead = await snapshotContext.ReadAsync(
                    new CourseReadSnapshotRequest(
                        courseId,
                        moodleAlias,
                        moodleUserId.Value.ToString(),
                        CourseReadSnapshotRequirements.Students | CourseReadSnapshotRequirements.Gradebook),
                    cancellationToken);
                if (courseRead is not null)
                {
                    effectiveCourseId = courseRead.CourseId;
                    prefetchedGradebook = courseRead.Gradebook?.Data;
                    if (courseRead.Students?.Data is { HasMore: false } participants &&
                        courseRead.Students.IsComplete)
                    {
                        prefetchedParticipants = participants;
                    }

                    var updatedAt = courseRead.Metadata.OldestUpdatedAt;
                    freshness = new ToolFreshness(
                        "snapshot",
                        updatedAt,
                        updatedAt.HasValue
                            ? Math.Max(0, (long)(DateTimeOffset.UtcNow - updatedAt.Value).TotalSeconds)
                            : null,
                        courseRead.Metadata.StaleDatasets.Count > 0,
                        courseRead.Metadata.RefreshQueued,
                        courseRead.Metadata.IsComplete,
                        (courseRead.Students?.RecordCount ?? 0) +
                        (courseRead.Gradebook?.RecordCount ?? 0));
                }
            }
            catch
            {
                // Snapshot warming is best effort; the current request uses
                // the live bulk gateway and its explicit fallback.
            }
        }

        GetStudentsBelowMinGradeResult data;
        try
        {
            data = await mediator.Send(
                new GetStudentsBelowMinGradeQuery(
                    effectiveCourseId,
                    minGradePercent,
                    maxStudentsToAnalyze,
                    PrefetchedGradebook: prefetchedGradebook,
                    PrefetchedParticipants: prefetchedParticipants),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException exception)
        {
            return ToolResultHelper.Error<GetStudentsBelowMinGradeResult>(exception);
        }
        catch (Exception exception)
        {
            return ToolResultHelper.Error<GetStudentsBelowMinGradeResult>(exception);
        }

        var response = new ToolResponse<GetStudentsBelowMinGradeResult>(
            "ok", data, [], AuditId: null, DateTimeOffset.UtcNow, Freshness: freshness);
        var narration = $"Análise do curso {courseId}: {data.TotalStudentsAnalyzed} estudante(s) analisado(s). " +
                        $"{data.Students.Count} com pelo menos uma SA abaixo de {data.MinGradePercent}%.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }
}
