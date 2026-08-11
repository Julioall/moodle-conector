using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Reports;

/// <summary>
/// Tools de relatÃ³rios pedagÃ³gicos para tutores SENAI CTM.
/// Fase 10 â€” DomÃ­nio RelatÃ³rios.
/// </summary>
[McpServerToolType]
public sealed class MoodleReportTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleConnector.Application.Abstractions.IMoodleReportBuilderGateway reportBuilderClient)
{
    // â”€â”€ RelatÃ³rio semanal de desempenho â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [McpServerTool(
        Name = "generate_weekly_performance_report",
        Title = "Generate Weekly Performance Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateWeeklyPerformanceReportResult>))]
    [Description("Gera relatÃ³rio semanal de desempenho da turma: cruza acesso ao AVA, notas por SA e entregas pendentes. Classifica cada estudante como 'ok', 'attention' ou 'risk'. Retorna 3 listas de destinatÃ¡rios sugeridos para envio de mensagem (acesso, nota e pendÃªncia). AVISO: uma consulta de boletim por estudante â€” pode ser lento para turmas grandes.")]
    public Task<CallToolResult> GerarRelatorioSemanalDesempenhoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Nota mÃ­nima esperada em porcentagem (0-100). PadrÃ£o: 60.")] decimal minGradePercent = 60m,
        [Description("Dias sem acesso para considerar inativo. PadrÃ£o: 7.")] int inactiveDaysThreshold = 7,
        [Description("MÃ¡ximo de estudantes a analisar. PadrÃ£o: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateWeeklyPerformanceReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateWeeklyPerformanceReportQuery(courseId, minGradePercent, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"RelatÃ³rio semanal â€” curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"{result.StudentsAtRisk} em risco, {result.StudentsWithAttention} em atenÃ§Ã£o. " +
                      $"Gerado em: {result.GeneratedAt:dd/MM/yyyy HH:mm} UTC.",
            cancellationToken);

    // â”€â”€ RelatÃ³rio de conselho de classe â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [McpServerTool(
        Name = "generate_class_council_report",
        Title = "Generate Class Council Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateClassCouncilReportResult>))]
    [Description("Gera relatÃ³rio de conselho de classe com situaÃ§Ã£o pedagÃ³gica indicativa de cada estudante: 'regular', 'attention', 'recovery_needed' ou 'at_risk'. ATENÃ‡ÃƒO: nÃ£o constitui decisÃ£o oficial de aprovaÃ§Ã£o ou reprovaÃ§Ã£o. Deve ser interpretado pelo tutor e docente presencial.")]
    public Task<CallToolResult> GerarRelatorioConselhoClasseAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Nota mÃ­nima esperada em porcentagem (0-100). PadrÃ£o: 60.")] decimal minGradePercent = 60m,
        [Description("Dias sem acesso para considerar inativo. PadrÃ£o: 7.")] int inactiveDaysThreshold = 7,
        [Description("MÃ¡ximo de estudantes a analisar. PadrÃ£o: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateClassCouncilReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateClassCouncilReportQuery(courseId, minGradePercent, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Conselho de classe - curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"Regular: {result.Regular} | AtenÃ§Ã£o: {result.NeedAttention} | RecuperaÃ§Ã£o: {result.NeedRecovery} | Risco: {result.AtRisk}.",
            cancellationToken);


    [McpServerTool(
        Name = "generate_course_summary",
        Title = "Generate Course Summary",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseOverviewResult>))]
    [Description("Gera resumo executivo e rÃ¡pido do curso: participantes ativos, acesso ao AVA e aÃ§Ãµes sugeridas. NÃ£o consulta boletim individual â€” use gerar_relatorio_semanal_desempenho para dados detalhados.")]
    public Task<CallToolResult> GerarResumoCursoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Dias sem acesso para considerar inativo. PadrÃ£o: 7.")] int inactiveDaysThreshold = 7,
        [Description("MÃ¡ximo de estudantes a analisar. PadrÃ£o: 100.")] int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<CourseOverviewResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateCourseOverviewQuery(courseId, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Resumo - curso {courseId}: {result.TotalActiveStudents} estudante(s). " +
                      $"{result.StudentsWhoAccessed} acessaram, {result.StudentsNeverAccessed} nunca acessaram, " +
                      $"{result.StudentsInactiveDays} inativos hÃ¡ +{result.InactiveDaysThreshold} dias.",
            cancellationToken);

    // â”€â”€ RelatÃ³rio de pÃ³s-execuÃ§Ã£o â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [McpServerTool(
        Name = "generate_full_post_execution_report",
        Title = "Generate Full Post-Execution Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GeneratePostExecutionReportResult>))]
    [Description("Gera relatÃ³rio de pÃ³s-execuÃ§Ã£o com situaÃ§Ã£o provÃ¡vel de cada estudante ao fim do curso: 'likely_complete', 'pending_recovery', 'at_risk' ou 'unknown'. ATENÃ‡ÃƒO: indicativo â€” nÃ£o constitui decisÃ£o oficial. Deve ser validado pelo tutor e coordenaÃ§Ã£o.")]
    public Task<CallToolResult> GerarRelatorioPosExecucaoCompletoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Nota mÃ­nima esperada em porcentagem (0-100). PadrÃ£o: 60.")] decimal minGradePercent = 60m,
        [Description("MÃ¡ximo de estudantes a analisar. PadrÃ£o: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GeneratePostExecutionReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GeneratePostExecutionReportQuery(courseId, minGradePercent, maxStudentsToAnalyze),
                cancellationToken),
            result => $"PÃ³s-execuÃ§Ã£o â€” curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"ProvÃ¡vel conclusÃ£o: {result.LikelyComplete} | RecuperaÃ§Ã£o: {result.PendingRecovery} | " +
                      $"Risco: {result.AtRisk} | Dados insuficientes: {result.Unknown}.",
            cancellationToken);

    
    [McpServerTool(
        Name = "download_moodle_builder_report",
        Title = "Download Moodle Builder Report",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<MoodleConnector.Application.Abstractions.MoodleReportResult>))]
    [Description("Baixa o JSON de qualquer relatÃ³rio personalizado do Moodle Report Builder acessÃ­vel ao usuÃ¡rio do token. Retorna os registros paginados limitados ao 'pageSize'.")]
    public async Task<CallToolResult> BaixarRelatorioBuilderAsync(
        [Description("Identificador numÃ©rico do relatÃ³rio.")] int reportId,
        [Description("DicionÃ¡rio opcional de filtros em formato JSON (ex: '{\"user:firstname_operator\":2, \"user:firstname_value\":\"JoÃ£o\"}').")] string? filtersJson = null,
        [Description("Quantidade mÃ¡xima de registros a retornar. PadrÃ£o: 5000.")] int pageSize = 5000,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<MoodleConnector.Application.Abstractions.MoodleReportResult>("UsuÃ¡rio nÃ£o autenticado.");

        MoodleConnector.Application.Abstractions.MoodleReportResult data;
        try
        {
            var filters = !string.IsNullOrWhiteSpace(filtersJson)
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(filtersJson)
                : null;
            data = await reportBuilderClient.DownloadAsync(reportId, pageSize, filters, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ToolResultHelper.Error<MoodleConnector.Application.Abstractions.MoodleReportResult>($"NÃ£o foi possÃ­vel baixar o relatÃ³rio: {ex.Message}");
        }

        var response = new ToolResponse<MoodleConnector.Application.Abstractions.MoodleReportResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"RelatÃ³rio {reportId} baixado com sucesso: {data.Rows.Count} registro(s) retornado(s)." }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }


    private async Task<CallToolResult> ExecuteReportAsync<TResult>(
        string courseId,
        string? moodleAlias,
        Func<Task<TResult>> execute,
        Func<TResult, string> narrate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<TResult>("Informe um identificador de curso vÃ¡lido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<TResult>("UsuÃ¡rio nÃ£o autenticado.");

        TResult data;
        try { data = await execute(); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<TResult>("NÃ£o foi possÃ­vel gerar o relatÃ³rio neste momento.");
        }

        var response = new ToolResponse<TResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narrate(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }
}
