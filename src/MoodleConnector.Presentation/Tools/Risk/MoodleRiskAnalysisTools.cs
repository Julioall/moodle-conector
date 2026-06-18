using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Risk.Queries;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Risk;

[McpServerToolType]
public sealed class MoodleRiskAnalysisTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "gerar_relatorio_risco_estudantes",
        Title = "Gerar Relatorio Risco Estudantes",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IReadOnlyList<StudentRiskReport>>))]
    [Description("Gera um relatorio cruzando inatividade, notas baixas e progresso pendente para identificar estudantes em risco no curso.")]
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

    [McpServerTool(
        Name = "report_students_at_risk",
        Title = "Report Students at Risk",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IReadOnlyList<StudentRiskReport>>))]
    [Description("Generates a report cross-referencing inactivity, low grades, and pending progress to identify students at risk in the course.")]
    public Task<CallToolResult> ReportStudentsAtRiskAsync(
        [Description("Moodle course identifier.")]
        string courseId,
        [Description("Maximum students to analyze (e.g. 20, 50, 100). Default: 50.")]
        int maxStudentsToAnalyze = 50,
        [Description("Inactivity threshold in days to consider as a risk factor. Default: 7.")]
        int inactivityThresholdDays = 7,
        [Description("Minimum expected grade in percentage (0-100) to consider as a risk factor. Default: 60.")]
        decimal minGradePercentage = 60m,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
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
            return Error<IReadOnlyList<StudentRiskReport>>("Informe um identificador de curso valido.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<IReadOnlyList<StudentRiskReport>>("Usuario nao autenticado para gerar relatorio.");
        }

        IReadOnlyList<StudentRiskReport> data;
        try
        {
            data = await mediator.Send(
                new GetStudentsAtRiskReportQuery(courseId, maxStudents, inactivityThresholdDays, minGradePercentage),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Error<IReadOnlyList<StudentRiskReport>>($"Nao foi possivel gerar o relatorio neste momento: {ex.Message}");
        }

        var response = new ToolResponse<IReadOnlyList<StudentRiskReport>>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        var altos = data.Count(d => d.RiskLevel == RiskLevel.Alto);
        var medios = data.Count(d => d.RiskLevel == RiskLevel.Medio);
        
        var narration = $"O relatorio de risco do curso {courseId} foi gerado com sucesso. " +
                        $"Foram identificados {data.Count} estudantes com algum nivel de risco, " +
                        $"sendo {altos} com risco Alto e {medios} com risco Medio.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static CallToolResult Error<T>(string message)
    {
        var response = new ToolResponse<T>(
            "error",
            default!,
            [message],
            null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }
}
