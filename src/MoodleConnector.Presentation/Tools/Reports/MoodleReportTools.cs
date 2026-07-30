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
/// Tools de relatórios pedagógicos para tutores SENAI CTM.
/// Fase 10 — Domínio Relatórios.
/// </summary>
[McpServerToolType]
public sealed class MoodleReportTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleConnector.Application.Abstractions.IMoodleReportBuilderGateway reportBuilderClient)
{
    // ── Relatório semanal de desempenho ───────────────────────────────────────

    [McpServerTool(
        Name = "gerar_relatorio_semanal_desempenho",
        Title = "Gerar Relatorio Semanal de Desempenho",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateWeeklyPerformanceReportResult>))]
    [Description("Gera relatório semanal de desempenho da turma: cruza acesso ao AVA, notas por SA e entregas pendentes. Classifica cada estudante como 'ok', 'attention' ou 'risk'. Retorna 3 listas de destinatários sugeridos para envio de mensagem (acesso, nota e pendência). AVISO: uma consulta de boletim por estudante — pode ser lento para turmas grandes.")]
    public Task<CallToolResult> GerarRelatorioSemanalDesempenhoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")] decimal minGradePercent = 60m,
        [Description("Dias sem acesso para considerar inativo. Padrão: 7.")] int inactiveDaysThreshold = 7,
        [Description("Máximo de estudantes a analisar. Padrão: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateWeeklyPerformanceReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateWeeklyPerformanceReportQuery(courseId, minGradePercent, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Relatório semanal — curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"{result.StudentsAtRisk} em risco, {result.StudentsWithAttention} em atenção. " +
                      $"Gerado em: {result.GeneratedAt:dd/MM/yyyy HH:mm} UTC.",
            cancellationToken);

    [McpServerTool(
        Name = "generate_weekly_performance_report",
        Title = "Generate Weekly Performance Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateWeeklyPerformanceReportResult>))]
    [Description("Generates a weekly performance report for the class: combines AVA access, per-SA grades, and pending submissions. Classifies each student as 'ok', 'attention', or 'risk'. Returns 3 suggested recipient lists for follow-up messages. NOTE: one gradebook call per student — may be slow for large cohorts.")]
    public Task<CallToolResult> GenerateWeeklyPerformanceReportAsync(
        [Description("Moodle course identifier.")] string courseId,
        [Description("Minimum grade percentage (0-100). Default: 60.")] decimal minGradePercent = 60m,
        [Description("Days without access threshold. Default: 7.")] int inactiveDaysThreshold = 7,
        [Description("Maximum students to analyze. Default: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateWeeklyPerformanceReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateWeeklyPerformanceReportQuery(courseId, minGradePercent, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Weekly report — course {courseId}: {result.TotalStudents} students. " +
                      $"{result.StudentsAtRisk} at risk, {result.StudentsWithAttention} need attention. " +
                      $"Generated: {result.GeneratedAt:dd/MM/yyyy HH:mm} UTC.",
            cancellationToken);

    // ── Relatório de conselho de classe ───────────────────────────────────────

    [McpServerTool(
        Name = "gerar_relatorio_turma_conselho_classe",
        Title = "Gerar Relatorio Turma Conselho de Classe",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateClassCouncilReportResult>))]
    [Description("Gera relatório de conselho de classe com situação pedagógica indicativa de cada estudante: 'regular', 'attention', 'recovery_needed' ou 'at_risk'. ATENÇÃO: não constitui decisão oficial de aprovação ou reprovação. Deve ser interpretado pelo tutor e docente presencial.")]
    public Task<CallToolResult> GerarRelatorioConselhoClasseAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")] decimal minGradePercent = 60m,
        [Description("Dias sem acesso para considerar inativo. Padrão: 7.")] int inactiveDaysThreshold = 7,
        [Description("Máximo de estudantes a analisar. Padrão: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateClassCouncilReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateClassCouncilReportQuery(courseId, minGradePercent, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Conselho de classe — curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"Regular: {result.Regular} | Atenção: {result.NeedAttention} | Recuperação: {result.NeedRecovery} | Risco: {result.AtRisk}.",
            cancellationToken);

    [McpServerTool(
        Name = "generate_class_council_report",
        Title = "Generate Class Council Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateClassCouncilReportResult>))]
    [Description("Generates a class council report with indicative pedagogical status per student: 'regular', 'attention', 'recovery_needed', or 'at_risk'. NOTE: does not constitute an official pass/fail decision.")]
    public Task<CallToolResult> GenerateClassCouncilReportAsync(
        [Description("Moodle course identifier.")] string courseId,
        [Description("Minimum grade percentage (0-100). Default: 60.")] decimal minGradePercent = 60m,
        [Description("Days without access threshold. Default: 7.")] int inactiveDaysThreshold = 7,
        [Description("Maximum students to analyze. Default: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateClassCouncilReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateClassCouncilReportQuery(courseId, minGradePercent, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Class council — course {courseId}: {result.TotalStudents} students. " +
                      $"Regular: {result.Regular} | Attention: {result.NeedAttention} | Recovery: {result.NeedRecovery} | At risk: {result.AtRisk}.",
            cancellationToken);

    // ── Resumo executivo do curso ──────────────────────────────────────────────

    [McpServerTool(
        Name = "gerar_resumo_curso",
        Title = "Gerar Resumo Executivo do Curso",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseOverviewResult>))]
    [Description("Gera resumo executivo e rápido do curso: participantes ativos, acesso ao AVA e ações sugeridas. Não consulta boletim individual — use gerar_relatorio_semanal_desempenho para dados detalhados.")]
    public Task<CallToolResult> GerarResumoCursoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Dias sem acesso para considerar inativo. Padrão: 7.")] int inactiveDaysThreshold = 7,
        [Description("Máximo de estudantes a analisar. Padrão: 100.")] int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<CourseOverviewResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateCourseOverviewQuery(courseId, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Resumo — curso {courseId}: {result.TotalActiveStudents} estudante(s). " +
                      $"{result.StudentsWhoAccessed} acessaram, {result.StudentsNeverAccessed} nunca acessaram, " +
                      $"{result.StudentsInactiveDays} inativos há +{result.InactiveDaysThreshold} dias.",
            cancellationToken);

    // ── Relatório de pós-execução ─────────────────────────────────────────────

    [McpServerTool(
        Name = "gerar_relatorio_pos_execucao_completo",
        Title = "Gerar Relatorio Pos Execucao Completo",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GeneratePostExecutionReportResult>))]
    [Description("Gera relatório de pós-execução com situação provável de cada estudante ao fim do curso: 'likely_complete', 'pending_recovery', 'at_risk' ou 'unknown'. ATENÇÃO: indicativo — não constitui decisão oficial. Deve ser validado pelo tutor e coordenação.")]
    public Task<CallToolResult> GerarRelatorioPosExecucaoCompletoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")] decimal minGradePercent = 60m,
        [Description("Máximo de estudantes a analisar. Padrão: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GeneratePostExecutionReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GeneratePostExecutionReportQuery(courseId, minGradePercent, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Pós-execução — curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"Provável conclusão: {result.LikelyComplete} | Recuperação: {result.PendingRecovery} | " +
                      $"Risco: {result.AtRisk} | Dados insuficientes: {result.Unknown}.",
            cancellationToken);

    // ── Report Builder Moodle ──────────────────────────────────────────────────
    
    [McpServerTool(
        Name = "baixar_relatorio_moodle_builder",
        Title = "Baixar Relatório do Report Builder",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<MoodleConnector.Application.Abstractions.MoodleReportResult>))]
    [Description("Baixa o JSON de qualquer relatório personalizado do Moodle Report Builder acessível ao usuário do token. Retorna os registros paginados limitados ao 'pageSize'.")]
    public async Task<CallToolResult> BaixarRelatorioBuilderAsync(
        [Description("Identificador numérico do relatório.")] int reportId,
        [Description("Dicionário opcional de filtros em formato JSON (ex: '{\"user:firstname_operator\":2, \"user:firstname_value\":\"João\"}').")] string? filtersJson = null,
        [Description("Quantidade máxima de registros a retornar. Padrão: 5000.")] int pageSize = 5000,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<MoodleConnector.Application.Abstractions.MoodleReportResult>("Usuário não autenticado.");

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
            return ToolResultHelper.Error<MoodleConnector.Application.Abstractions.MoodleReportResult>($"Não foi possível baixar o relatório: {ex.Message}");
        }

        var response = new ToolResponse<MoodleConnector.Application.Abstractions.MoodleReportResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Relatório {reportId} baixado com sucesso: {data.Rows.Count} registro(s) retornado(s)." }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    [McpServerTool(
        Name = "download_moodle_builder_report",
        Title = "Download Moodle Builder Report",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<MoodleConnector.Application.Abstractions.MoodleReportResult>))]
    [Description("Downloads any custom Moodle Report Builder report accessible to the token user and returns its rows in JSON format, paginated up to 'pageSize'.")]
    public async Task<CallToolResult> DownloadBuilderReportAsync(
        [Description("Numeric identifier of the report.")] int reportId,
        [Description("Optional JSON string of filters (e.g., '{\"user:firstname_operator\":2, \"user:firstname_value\":\"João\"}').")] string? filtersJson = null,
        [Description("Maximum number of rows to return. Default: 5000.")] int pageSize = 5000,
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<MoodleConnector.Application.Abstractions.MoodleReportResult>("Usuário não autenticado.");

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
            return ToolResultHelper.Error<MoodleConnector.Application.Abstractions.MoodleReportResult>($"Failed to download report: {ex.Message}");
        }

        var response = new ToolResponse<MoodleConnector.Application.Abstractions.MoodleReportResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Report {reportId} downloaded successfully: {data.Rows.Count} row(s) returned." }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    [McpServerTool(
        Name = "listar_relatorios_moodle_builder",
        Title = "Listar Relatorios do Report Builder",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>))]
    [Description("Lista os relatórios personalizados acessíveis ao usuário associado ao token.")]
    public async Task<CallToolResult> ListarRelatoriosBuilderAsync(
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>("Usuário não autenticado.");

        IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo> data;
        try
        {
            data = await reportBuilderClient.ListReportsAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ToolResultHelper.Error<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>($"Não foi possível listar os relatórios: {ex.Message}");
        }

        var response = new ToolResponse<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"{data.Count} relatório(s) encontrado(s)." }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    [McpServerTool(
        Name = "list_moodle_builder_reports",
        Title = "List Moodle Builder Reports",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>))]
    [Description("Lists available custom Moodle Report Builder reports accessible to the token user.")]
    public async Task<CallToolResult> ListBuilderReportsAsync(
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>("Usuário não autenticado.");

        IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo> data;
        try
        {
            data = await reportBuilderClient.ListReportsAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return ToolResultHelper.Error<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>($"Failed to list reports: {ex.Message}");
        }

        var response = new ToolResponse<IReadOnlyList<MoodleConnector.Application.Abstractions.MoodleReportInfo>>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"{data.Count} report(s) found." }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    // ── Core helper ──────────────────────────────────────────────────────────

    private async Task<CallToolResult> ExecuteReportAsync<TResult>(
        string courseId,
        string? moodleAlias,
        Func<Task<TResult>> execute,
        Func<TResult, string> narrate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<TResult>("Informe um identificador de curso válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<TResult>("Usuário não autenticado.");

        TResult data;
        try { data = await execute(); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<TResult>("Não foi possível gerar o relatório neste momento.");
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
