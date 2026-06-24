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
/// Fase 20 — Domínio Monitor.
/// </summary>
[McpServerToolType]
public sealed class MoodleMonitorTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    // ── Auditar checklist da sala virtual ─────────────────────────────────────

    [McpServerTool(
        Name = "auditar_checklist_sala_virtual",
        Title = "Auditar Checklist Sala Virtual",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AuditVirtualClassroomChecklistResult>))]
    [Description("Audita a sala virtual do AVA contra o checklist padrão do Guia do Tutor SENAI CTM. Verifica presença de: Guia do Estudante, Critérios de Certificação, Plano de Estudo/Cronograma, Fórum de Apresentação, Fórum de Dúvidas, conteúdo interativo (SCORM), Situação de Aprendizagem (SA), datas configuradas e visibilidade da sala. Retorna status por item: ok, ausente, incompleto ou nao_verificavel.")]
    public Task<CallToolResult> AuditarChecklistSalaVirtualAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteMonitorAsync<AuditVirtualClassroomChecklistResult>(
            courseId, moodleAlias,
            () => mediator.Send(new AuditVirtualClassroomChecklistQuery(courseId), cancellationToken),
            result => $"Checklist da sala — curso {courseId}: {result.TotalItems} itens. " +
                      $"✅ OK: {result.OkCount} | ❌ Ausente: {result.AusenteCount} | " +
                      $"⚠️ Incompleto: {result.IncompletoCount} | ❓ Não verificável: {result.NaoVerificavelCount}.",
            cancellationToken);

    [McpServerTool(
        Name = "audit_virtual_classroom_checklist",
        Title = "Audit Virtual Classroom Checklist",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AuditVirtualClassroomChecklistResult>))]
    [Description("Audits the AVA virtual classroom against the SENAI CTM Tutor Guide standard checklist. Checks: Student Guide, Certification Criteria, Study Plan/Schedule, Welcome Forum, Q&A Forum, interactive content (SCORM), Learning Situations (SA), configured dates, and room visibility. Returns per-item status: ok, ausente, incompleto, or nao_verificavel.")]
    public Task<CallToolResult> AuditVirtualClassroomChecklistAsync(
        [Description("Moodle course identifier.")] string courseId,
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteMonitorAsync<AuditVirtualClassroomChecklistResult>(
            courseId, moodleAlias,
            () => mediator.Send(new AuditVirtualClassroomChecklistQuery(courseId), cancellationToken),
            result => $"Classroom checklist — course {courseId}: {result.TotalItems} items. " +
                      $"✅ OK: {result.OkCount} | ❌ Missing: {result.AusenteCount} | " +
                      $"⚠️ Incomplete: {result.IncompletoCount} | ❓ Not verifiable: {result.NaoVerificavelCount}.",
            cancellationToken);

    // ── Relatório administrativo da turma ─────────────────────────────────────

    [McpServerTool(
        Name = "gerar_relatorio_monitor_turma",
        Title = "Gerar Relatorio Monitor Turma",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateMonitorTurmaReportResult>))]
    [Description("Gera relatório administrativo da turma para o monitor: estudantes que nunca acessaram o AVA e estudantes inativos há mais de N dias. NÃO inclui notas ou submissões — essas informações são responsabilidade do tutor. Foco em acesso e matrícula.")]
    public Task<CallToolResult> GerarRelatorioMonitorTurmaAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Dias sem acesso para considerar inativo. Padrão: 7.")] int inactiveDaysThreshold = 7,
        [Description("Máximo de estudantes a analisar. Padrão: 100.")] int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteMonitorAsync<GenerateMonitorTurmaReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateMonitorTurmaReportQuery(courseId, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Relatório monitor — curso {courseId}: {result.TotalEnrolled} matriculado(s). " +
                      $"Acessaram: {result.StudentsWhoAccessed} | Nunca acessaram: {result.StudentsNeverAccessed} | " +
                      $"Inativos +{result.InactiveDaysThreshold}d: {result.StudentsInactiveDays}.",
            cancellationToken);

    [McpServerTool(
        Name = "generate_monitor_class_report",
        Title = "Generate Monitor Class Report",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GenerateMonitorTurmaReportResult>))]
    [Description("Generates an administrative class report for the monitor: students who never accessed the AVA and students inactive for more than N days. Does NOT include grades or submissions — those are the tutor's responsibility.")]
    public Task<CallToolResult> GenerateMonitorClassReportAsync(
        [Description("Moodle course identifier.")] string courseId,
        [Description("Days without access threshold. Default: 7.")] int inactiveDaysThreshold = 7,
        [Description("Maximum students to analyze. Default: 100.")] int maxStudentsToAnalyze = 100,
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ExecuteMonitorAsync<GenerateMonitorTurmaReportResult>(
            courseId, moodleAlias,
            () => mediator.Send(
                new GenerateMonitorTurmaReportQuery(courseId, inactiveDaysThreshold, maxStudentsToAnalyze),
                cancellationToken),
            result => $"Monitor report — course {courseId}: {result.TotalEnrolled} enrolled. " +
                      $"Accessed: {result.StudentsWhoAccessed} | Never: {result.StudentsNeverAccessed} | " +
                      $"Inactive +{result.InactiveDaysThreshold}d: {result.StudentsInactiveDays}.",
            cancellationToken);

    // ── Core helper ───────────────────────────────────────────────────────────

    private async Task<CallToolResult> ExecuteMonitorAsync<TResult>(
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
            return ToolResultHelper.Error<TResult>("Não foi possível gerar o relatório de monitor neste momento.");
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
