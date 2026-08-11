using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Monitor.Queries;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Monitor;

/// <summary>
/// Tools de suporte administrativo para o monitor SENAI CTM.
/// Fase 20 â€” DomÃ­nio Monitor.
/// </summary>
[McpServerToolType]
public sealed class MoodleMonitorTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{

    [McpServerTool(
        Name = "audit_virtual_classroom_checklist",
        Title = "Audit Virtual Classroom Checklist",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AuditVirtualClassroomChecklistResult>))]
    [Description("Audita a sala virtual do AVA contra o checklist padrÃ£o do Guia do Tutor SENAI CTM. Verifica presenÃ§a de: Guia do Estudante, CritÃ©rios de CertificaÃ§Ã£o, Plano de Estudo/Cronograma, FÃ³rum de ApresentaÃ§Ã£o, FÃ³rum de DÃºvidas, conteÃºdo interativo (SCORM), SituaÃ§Ã£o de Aprendizagem (SA), datas configuradas e visibilidade da sala. Retorna status por item: ok, ausente, incompleto ou nao_verificavel.")]
    public Task<CallToolResult> AuditarChecklistSalaVirtualAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteMonitorAsync<AuditVirtualClassroomChecklistResult>(
            courseId, moodleAlias,
            () => mediator.Send(new AuditVirtualClassroomChecklistQuery(courseId), cancellationToken),
            result => $"Checklist da sala - curso {courseId}: {result.TotalItems} itens. " +
                      $"? OK: {result.OkCount} | ? Ausente: {result.AusenteCount} | " +
                      $"âš ï¸ Incompleto: {result.IncompletoCount} | â“ NÃ£o verificÃ¡vel: {result.NaoVerificavelCount}.",
            cancellationToken);

    // â”€â”€ RelatÃ³rio administrativo da turma â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [McpServerTool(
        Name = "generate_monitor_class_report",
        Title = "Generate Monitor Class Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateMonitorTurmaReportResult>))]
    [Description("Gera relatÃ³rio administrativo da turma para o monitor: estudantes que nunca acessaram o AVA e estudantes inativos hÃ¡ mais de N dias. NÃƒO inclui notas ou submissÃµes â€” essas informaÃ§Ãµes sÃ£o responsabilidade do tutor. Foco em acesso e matrÃ­cula.")]
    public Task<CallToolResult> GerarRelatorioMonitorTurmaAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Dias sem acesso para considerar inativo. PadrÃ£o: 7.")] int inactiveDaysThreshold = 7,
        [Description("MÃ¡ximo de estudantes a analisar. PadrÃ£o: 100.")] int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteMonitorAsync<GenerateMonitorTurmaReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateMonitorTurmaReportQuery(courseId, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"RelatÃ³rio monitor â€” curso {courseId}: {result.TotalEnrolled} matriculado(s). " +
                      $"Acessaram: {result.StudentsWhoAccessed} | Nunca acessaram: {result.StudentsNeverAccessed} | " +
                      $"Inativos +{result.InactiveDaysThreshold}d: {result.StudentsInactiveDays}.",
            cancellationToken);


    private async Task<CallToolResult> ExecuteMonitorAsync<TResult>(
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
            return ToolResultHelper.Error<TResult>("NÃ£o foi possÃ­vel gerar o relatÃ³rio de monitor neste momento.");
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
