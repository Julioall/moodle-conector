using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure.Reports;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Presentation.Tools.Reports;

public sealed record CourseGradesExcelExportResult(
    string CourseId,
    DateTimeOffset GeneratedAt,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    int TotalStudents,
    int StudentsWithGrade,
    int StudentsWithoutGrade,
    decimal? AveragePercentage,
    string? Warning);

/// <summary>
/// Tools de relatórios pedagógicos para tutores.
/// Fase 10 — Domínio Relatórios.
/// </summary>
[McpServerToolType]
public sealed class MoodleReportTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleSnapshotToolContext? snapshotContext = null,
    IMoodleCourseReadSnapshotCoordinator? snapshotCoordinator = null)
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [McpServerTool(
        Name = "generate_course_grades_report",
        Title = "Generate Course Grades Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateCourseGradesReportResult>))]
    [Description("Gera um relatorio estruturado de notas do curso por estudante. Usa o item total do curso retornado pelo Moodle; nao soma notas de atividades localmente. Para um arquivo Excel, use export_course_grades_excel.")]
    public Task<CallToolResult> GerarRelatorioNotasCursoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Tamanho das paginas usadas na leitura de participantes. De 1 a 100. Padrao: 100.")] int pageSize = 100,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateCourseGradesReportResult>(
            courseId,
            moodleAlias,
            (effectiveCourseId, gradebook, participants) => mediator.Send(
                new GenerateCourseGradesReportQuery(
                    effectiveCourseId,
                    pageSize,
                    PrefetchedGradebook: gradebook,
                    PrefetchedParticipants: participants),
                cancellationToken),
            result => $"Relatorio de notas - curso {courseId}: {result.TotalStudents} estudante(s), " +
                      $"{result.StudentsWithGrade} com nota e {result.StudentsWithoutGrade} sem nota.",
            cancellationToken,
            requirements: CourseReadSnapshotRequirements.Students | CourseReadSnapshotRequirements.Gradebook);

    [McpServerTool(
        Name = "export_course_grades_excel",
        Title = "Export Course Grades to Excel",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseGradesExcelExportResult>))]
    [Description("Gera e entrega um arquivo Excel formatado com as notas totais do curso por estudante. O arquivo e anexado ao resultado MCP; a regra de notas e a mesma usada pelo relatorio Excel do frontend.")]
    public async Task<CallToolResult> ExportarRelatorioNotasExcelAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Tamanho das paginas usadas na leitura de participantes. De 1 a 100. Padrao: 100.")] int pageSize = 100,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<CourseGradesExcelExportResult>("Informe um identificador de curso valido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<CourseGradesExcelExportResult>("Usuario nao autenticado.");

        try
        {
            var effectiveCourseId = courseId;
            CourseGradebookSnapshot? prefetchedGradebook = null;
            CourseParticipantsPage? prefetchedParticipants = null;
            var coordinator = snapshotCoordinator ?? snapshotContext as IMoodleCourseReadSnapshotCoordinator;
            if (coordinator is not null)
            {
                var courseRead = await coordinator.ReadAsync(
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
                }
            }
            var report = await mediator.Send(
                new GenerateCourseGradesReportQuery(
                    effectiveCourseId,
                    pageSize,
                    PrefetchedGradebook: prefetchedGradebook,
                    PrefetchedParticipants: prefetchedParticipants),
                cancellationToken);
            var fileName = $"relatorio_notas_{Slugify(courseId)}_{report.GeneratedAt:yyyyMMdd-HHmmss}.xlsx";
            var workbook = ExcelGradeReportBuilder.BuildWorkbook(
                courseId,
                report.GeneratedAt,
                [new ExcelGradeUnit(courseId, courseId, report.Students, report.Warning)]);
            var data = new CourseGradesExcelExportResult(
                report.CourseId,
                report.GeneratedAt,
                fileName,
                ExcelContentType,
                workbook.LongLength,
                report.TotalStudents,
                report.StudentsWithGrade,
                report.StudentsWithoutGrade,
                report.AveragePercentage,
                report.Warning);
            var response = new ToolResponse<CourseGradesExcelExportResult>(
                "ok",
                data,
                report.Warning is null ? [] : [report.Warning],
                AuditId: null,
                DateTimeOffset.UtcNow);
            var resource = BlobResourceContents.FromBytes(
                workbook,
                $"mcp://moodle-connector/reports/{Guid.NewGuid():N}/{fileName}",
                ExcelContentType);

            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = $"Arquivo Excel de notas gerado: {fileName} ({report.TotalStudents} estudante(s))." },
                    new EmbeddedResourceBlock { Resource = resource }
                ],
                StructuredContent = JsonSerializer.SerializeToElement(response),
                IsError = false
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return ToolResultHelper.Error<CourseGradesExcelExportResult>(exception);
        }
    }

    // â”€â”€ Relatório semanal de desempenho â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [McpServerTool(
        Name = "generate_weekly_performance_report",
        Title = "Generate Weekly Performance Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateWeeklyPerformanceReportResult>))]
    [Description("Gera relatório semanal de desempenho da turma: cruza acesso ao AVA, notas por SA e entregas pendentes. Classifica cada estudante como 'ok', 'attention' ou 'risk'. Retorna 3 listas de destinatários sugeridos para envio de mensagem (acesso, nota e pendência). A leitura do boletim usa uma consulta agregada por curso, com fallback seguro quando a instalação não oferece a capacidade bulk.")]
    public Task<CallToolResult> GerarRelatorioSemanalDesempenhoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")] decimal minGradePercent = 60m,
        [Description("Dias sem acesso para considerar inativo. Padrão: 7.")] int inactiveDaysThreshold = 7,
        [Description("Máximo de estudantes a analisar. Padrão: 60.")] int maxStudentsToAnalyze = 60,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteReportAsync<GenerateWeeklyPerformanceReportResult>(
            courseId, moodleAlias,
            (effectiveCourseId, gradebook, participants) => mediator.Send(
                new GenerateWeeklyPerformanceReportQuery(
                    effectiveCourseId,
                    minGradePercent,
                    inactiveDaysThreshold,
                    maxStudentsToAnalyze,
                    PrefetchedGradebook: gradebook,
                    PrefetchedParticipants: participants),
                cancellationToken),
            result => $"Relatório semanal — curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"{result.StudentsAtRisk} em risco, {result.StudentsWithAttention} em atenção. " +
                      $"Gerado em: {result.GeneratedAt:dd/MM/yyyy HH:mm} UTC.",
            cancellationToken,
            requirements: CourseReadSnapshotRequirements.Students | CourseReadSnapshotRequirements.Gradebook);

    // â”€â”€ Relatório de conselho de classe â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [McpServerTool(
        Name = "generate_class_council_report",
        Title = "Generate Class Council Report",
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
            (effectiveCourseId, gradebook, participants) => mediator.Send(
                new GenerateClassCouncilReportQuery(
                    effectiveCourseId,
                    minGradePercent,
                    inactiveDaysThreshold,
                    maxStudentsToAnalyze,
                    PrefetchedGradebook: gradebook,
                    PrefetchedParticipants: participants),
                cancellationToken),
            result => $"Conselho de classe - curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"Regular: {result.Regular} | Atenção: {result.NeedAttention} | Recuperação: {result.NeedRecovery} | Risco: {result.AtRisk}.",
            cancellationToken,
            requirements: CourseReadSnapshotRequirements.Students | CourseReadSnapshotRequirements.Gradebook);


    [McpServerTool(
        Name = "generate_course_summary",
        Title = "Generate Course Summary",
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
            (effectiveCourseId, _, _) => mediator.Send(
                new GenerateCourseOverviewQuery(effectiveCourseId, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Resumo - curso {courseId}: {result.TotalActiveStudents} estudante(s). " +
                      $"{result.StudentsWhoAccessed} acessaram, {result.StudentsNeverAccessed} nunca acessaram, " +
                      $"{result.StudentsInactiveDays} inativos há +{result.InactiveDaysThreshold} dias.",
            cancellationToken);

    // â”€â”€ Relatório de pós-execução â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [McpServerTool(
        Name = "generate_full_post_execution_report",
        Title = "Generate Full Post-Execution Report",
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
            (effectiveCourseId, gradebook, participants) => mediator.Send(
                new GeneratePostExecutionReportQuery(
                    effectiveCourseId,
                    minGradePercent,
                    maxStudentsToAnalyze,
                    PrefetchedGradebook: gradebook,
                    PrefetchedParticipants: participants),
                cancellationToken),
            result => $"Pós-execução — curso {courseId}: {result.TotalStudents} estudante(s). " +
                      $"Provável conclusão: {result.LikelyComplete} | Recuperação: {result.PendingRecovery} | " +
                      $"Risco: {result.AtRisk} | Dados insuficientes: {result.Unknown}.",
            cancellationToken,
            requirements: CourseReadSnapshotRequirements.Students | CourseReadSnapshotRequirements.Gradebook);

    
    [Description("Baixa o JSON de qualquer relatório personalizado do Moodle Report Builder acessível ao usuário do token. Retorna os registros paginados limitados ao 'pageSize'.")]
 #if false
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


 #endif

    private static string Slugify(string value)
    {
        var slug = new string(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
        slug = string.Join('_', slug.Split('_', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "curso" : slug[..Math.Min(slug.Length, 60)];
    }

    private async Task<CallToolResult> ExecuteReportAsync<TResult>(
        string courseId,
        string? moodleAlias,
        Func<string, CourseGradebookSnapshot?, CourseParticipantsPage?, Task<TResult>> execute,
        Func<TResult, string> narrate,
        CancellationToken cancellationToken,
        CourseReadSnapshotRequirements requirements = CourseReadSnapshotRequirements.None)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<TResult>("Informe um identificador de curso válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<TResult>("Usuário não autenticado.");

        CourseGradebookSnapshot? prefetchedGradebook = null;
        CourseParticipantsPage? prefetchedParticipants = null;
        var effectiveCourseId = courseId;
        ToolFreshness? freshness = null;
        if (requirements != CourseReadSnapshotRequirements.None)
        {
            try
            {
                var coordinator = snapshotCoordinator ?? snapshotContext as IMoodleCourseReadSnapshotCoordinator;
                if (coordinator is not null)
                {
                    var courseRead = await coordinator.ReadAsync(
                        new CourseReadSnapshotRequest(
                            courseId,
                            moodleAlias,
                            moodleUserId.Value.ToString(),
                            requirements),
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
                        var recordCount = (courseRead.Students?.RecordCount ?? 0) +
                            (courseRead.Gradebook?.RecordCount ?? 0);
                        freshness = new ToolFreshness(
                            "snapshot",
                            updatedAt,
                            updatedAt.HasValue
                                ? Math.Max(0, (long)(DateTimeOffset.UtcNow - updatedAt.Value).TotalSeconds)
                                : null,
                            courseRead.Metadata.StaleDatasets.Count > 0,
                            courseRead.Metadata.RefreshQueued,
                            courseRead.Metadata.IsComplete,
                            recordCount);
                    }
                }
            }
            catch
            {
                // Warming is best effort; the current report still uses the
                // bulk gateway and its capability-driven fallback.
            }
        }

        TResult data;
        try { data = await execute(effectiveCourseId, prefetchedGradebook, prefetchedParticipants); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<TResult>("Não foi possível gerar o relatório neste momento.");
        }

        var response = new ToolResponse<TResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow, Freshness: freshness);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narrate(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }
}
