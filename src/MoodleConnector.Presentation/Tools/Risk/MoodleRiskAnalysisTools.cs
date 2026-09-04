using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Risk.Queries;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Presentation.Tools.Risk;

[McpServerToolType]
public sealed class MoodleRiskAnalysisTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleSnapshotToolContext? snapshotContext = null)
{
    [McpServerTool(
        Name = "report_students_at_risk",
        Title = "Report Students At Risk",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IReadOnlyList<StudentRiskReport>>))]
    [Description("Gera um relatorio cruzando inatividade e notas baixas para identificar estudantes em risco no curso. Completion detalhado nao e consultado.")]
    public Task<CallToolResult> GerarRelatorioRiscoEstudantesAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Maximo de estudantes para analisar (ex: 20, 50, 100). Padrao: 50.")]
        int maxStudentsToAnalyze = 50,
        [Description("Limite de dias de inatividade para considerar como fator de risco. Padrao: 7.")]
        int inactivityThresholdDays = 7,
        [Description("Nota minima esperada em porcentagem (0-100) para considerar como fator de risco. Padrao: 60.")]
        decimal minGradePercentage = 60m,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetRiskReportCoreAsync(
            courseId,
            maxStudentsToAnalyze,
            inactivityThresholdDays,
            minGradePercentage,
            moodleAlias,
            cancellationToken);
    }

    private async Task<CallToolResult> GetRiskReportCoreAsync(
        string courseId,
        int maxStudents,
        int inactivityThresholdDays,
        decimal minGradePercentage,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<IReadOnlyList<StudentRiskReport>>("Informe um identificador de curso valido.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<IReadOnlyList<StudentRiskReport>>("Usuario nao autenticado para gerar relatorio.");
        }

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
                // Snapshot warming is best effort; the current report keeps
                // its live bulk/fallback behavior.
            }
        }

        StudentsAtRiskReportResult result;
        try
        {
            result = await mediator.Send(
                new GetStudentsAtRiskReportQuery(
                    effectiveCourseId,
                    maxStudents,
                    inactivityThresholdDays,
                    minGradePercentage,
                    PrefetchedGradebook: prefetchedGradebook,
                    PrefetchedParticipants: prefetchedParticipants),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<IReadOnlyList<StudentRiskReport>>("Nao foi possivel gerar o relatorio neste momento.");
        }

        var data = result.Reports;
        var response = new ToolResponse<IReadOnlyList<StudentRiskReport>>(
            "ok",
            data,
            BuildWarnings(result),
            AuditId: null,
            DateTimeOffset.UtcNow,
            Freshness: freshness);

        var altos = data.Count(d => d.RiskLevel == RiskLevel.Alto);
        var medios = data.Count(d => d.RiskLevel == RiskLevel.Medio);

        var narration = $"O relatorio de risco do curso {courseId} analisou {result.ParticipantsAnalyzedCount} participante(s). " +
                        $"Foram identificados {data.Count} estudante(s) com algum nivel de risco, " +
                        $"sendo {altos} com risco Alto e {medios} com risco Medio.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static IReadOnlyList<string> BuildWarnings(StudentsAtRiskReportResult result)
    {
        var warnings = new List<string>();

        if (result.ClassificationDiagnostics.IncludedByFallbackCount > 0)
        {
            warnings.Add(
                "Nao foi possivel identificar todos os alunos por role. " +
                $"{result.ClassificationDiagnostics.IncludedByFallbackCount} participante(s) foram incluidos por fallback no relatorio.");
        }

        if (result.ParticipantsAnalyzedCount == 0)
        {
            warnings.Add("O curso nao retornou participantes para analise de risco.");
        }
        else if (result.Reports.Count == 0)
        {
            warnings.Add(
                $"Foram analisados {result.ParticipantsAnalyzedCount} participante(s), " +
                "mas nenhum fator de risco configurado foi detectado.");
        }

        if (result.GradebookFailureCount > 0)
        {
            warnings.Add(
                $"Nao foi possivel consultar notas de {result.GradebookFailureCount} participante(s); " +
                "o relatorio pode estar parcial.");
        }

        return warnings;
    }


}
