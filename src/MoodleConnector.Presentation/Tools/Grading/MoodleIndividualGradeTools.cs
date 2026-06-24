using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Grading;

/// <summary>
/// Tools de lançamento individual de nota com confirmação humana obrigatória.
/// Fase 14 (parcial) — Domínio Notas.
/// Feature flag: AssignmentGradeWriteEnabled.
/// Escopo: moodle.write.assignments.grade.
/// Risco: CriticalHumanConfirmedWrite.
/// </summary>
[McpServerToolType]
public sealed class MoodleIndividualGradeTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    // ── Preparar lançamento de nota ───────────────────────────────────────────

    [McpServerTool(
        Name = "preparar_lancamento_nota",
        Title = "Preparar Lancamento de Nota Individual",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IndividualGradePrepareResult>))]
    [Description("Prepara o lançamento de nota individual para um estudante em uma atividade (SA). Busca a nota atual para comparação. Retorna uma prévia com os riscos e o texto exato de confirmação necessário. ATENÇÃO: requer feature flag AssignmentGradeWriteEnabled e escopo moodle.write.assignments.grade. Use confirmar_lancamento_nota para executar após revisar a prévia.")]
    public async Task<CallToolResult> PrepararLancamentoNotaAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Identificador da atividade (assign) no Moodle.")] string assignmentId,
        [Description("Identificador do estudante no Moodle.")] string studentId,
        [Description("Nota a ser lançada (número decimal, ex: 8.5).")] decimal proposedGrade,
        [Description("Justificativa obrigatória para o lançamento.")] string justification,
        [Description("Feedback textual a publicar junto com a nota (opcional).")] string? feedbackText = null,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(assignmentId) || string.IsNullOrWhiteSpace(studentId))
            return ToolResultHelper.Error<IndividualGradePrepareResult>("courseId, assignmentId e studentId são obrigatórios.");
        if (string.IsNullOrWhiteSpace(justification))
            return ToolResultHelper.Error<IndividualGradePrepareResult>("Uma justificativa é obrigatória para o lançamento de nota.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<IndividualGradePrepareResult>("Usuário não autenticado.");

        IndividualGradePrepareResult result;
        try
        {
            result = await mediator.Send(
                new PrepareIndividualGradeCommand(courseId, assignmentId, studentId, proposedGrade, feedbackText, justification),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<IndividualGradePrepareResult>(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ToolResultHelper.Error<IndividualGradePrepareResult>(ex.Message);
        }

        var response = new ToolResponse<IndividualGradePrepareResult>("pending", result, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = $"Prévia de nota preparada para {result.Preview.StudentFullName} — atividade {assignmentId}. " +
                       $"Nota proposta: {result.Preview.ProposedGrade:F2}. " +
                       (result.Preview.PreviousGrade.HasValue
                           ? $"Nota atual: {result.Preview.PreviousGrade.Value:F2}. "
                           : "Sem nota anterior registrada. ") +
                       $"Para confirmar, use confirmar_lancamento_nota com id {result.PendingActionId} e o texto: '{result.Preview.ConfirmationText}'."
            }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    [McpServerTool(
        Name = "prepare_individual_grade_launch",
        Title = "Prepare Individual Grade Launch",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IndividualGradePrepareResult>))]
    [Description("Prepares an individual grade submission for a student on an assignment (SA). Fetches the current grade for comparison. Returns a preview with risks and exact confirmation text. CAUTION: requires AssignmentGradeWriteEnabled feature flag and moodle.write.assignments.grade scope. Use confirm_individual_grade_launch to execute after reviewing the preview.")]
    public async Task<CallToolResult> PrepareIndividualGradeLaunchAsync(
        [Description("Moodle course identifier.")] string courseId,
        [Description("Moodle assignment identifier.")] string assignmentId,
        [Description("Moodle student identifier.")] string studentId,
        [Description("Grade to submit (decimal, e.g. 8.5).")] decimal proposedGrade,
        [Description("Mandatory justification for the grade submission.")] string justification,
        [Description("Optional feedback text to publish with the grade.")] string? feedbackText = null,
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(assignmentId) || string.IsNullOrWhiteSpace(studentId))
            return ToolResultHelper.Error<IndividualGradePrepareResult>("courseId, assignmentId and studentId are required.");
        if (string.IsNullOrWhiteSpace(justification))
            return ToolResultHelper.Error<IndividualGradePrepareResult>("A justification is required for grade submission.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<IndividualGradePrepareResult>("User not authenticated.");

        IndividualGradePrepareResult result;
        try
        {
            result = await mediator.Send(
                new PrepareIndividualGradeCommand(courseId, assignmentId, studentId, proposedGrade, feedbackText, justification),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<IndividualGradePrepareResult>(ex.Message);
        }

        var response = new ToolResponse<IndividualGradePrepareResult>("pending", result, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = $"Grade preview prepared for {result.Preview.StudentFullName} — assignment {assignmentId}. " +
                       $"Proposed grade: {result.Preview.ProposedGrade:F2}. " +
                       $"To confirm, use confirm_individual_grade_launch with id {result.PendingActionId} and text: '{result.Preview.ConfirmationText}'."
            }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    // ── Confirmar lançamento de nota ──────────────────────────────────────────

    [McpServerTool(
        Name = "confirmar_lancamento_nota",
        Title = "Confirmar Lancamento de Nota Individual",
        ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IndividualGradeSendResult>))]
    [Description("Confirma e executa o lançamento de nota individual previamente preparado. Exige o texto exato de confirmação (incluindo a nota numérica) retornado pela ferramenta preparar_lancamento_nota. CRÍTICO: esta ação é irreversível — a nota será lançada imediatamente no Moodle e visível ao estudante.")]
    public async Task<CallToolResult> ConfirmarLancamentoNotaAsync(
        [Description("ID da ação pendente retornado por preparar_lancamento_nota.")] Guid pendingActionId,
        [Description("Texto exato de confirmação retornado na prévia (ex: 'CONFIRMAR NOTA 8.50').")] string confirmationText,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(confirmationText))
            return ToolResultHelper.Error<IndividualGradeSendResult>("O texto de confirmação é obrigatório.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<IndividualGradeSendResult>("Usuário não autenticado.");

        IndividualGradeSendResult result;
        try
        {
            result = await mediator.Send(
                new ConfirmIndividualGradeCommand(pendingActionId, confirmationText),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<IndividualGradeSendResult>(ex.Message);
        }

        var isError = result.Status is "failed" or "rejected";
        var response = new ToolResponse<IndividualGradeSendResult>(result.Status, result, [], result.AuditId, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = result.Status switch
                {
                    "launched" => $"Nota {result.LaunchedGrade:F2} lançada com sucesso para estudante {result.StudentId} na atividade {result.AssignmentId}. AuditId: {result.AuditId}.",
                    "partial"  => $"Nota {result.LaunchedGrade:F2} lançada com status parcial. Verifique no Moodle. AuditId: {result.AuditId}.",
                    "rejected" => $"Confirmação rejeitada: {string.Join("; ", result.Warnings)}",
                    _          => $"Falha ao lançar nota: {string.Join("; ", result.Warnings)}"
                }
            }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = isError
        };
    }

    [McpServerTool(
        Name = "confirm_individual_grade_launch",
        Title = "Confirm Individual Grade Launch",
        ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IndividualGradeSendResult>))]
    [Description("Confirms and executes the individual grade submission previously prepared. Requires the exact confirmation text (including the numeric grade) returned by prepare_individual_grade_launch. CRITICAL: irreversible — grade is immediately visible to the student in Moodle.")]
    public async Task<CallToolResult> ConfirmIndividualGradeLaunchAsync(
        [Description("Pending action ID returned by prepare_individual_grade_launch.")] Guid pendingActionId,
        [Description("Exact confirmation text from the preview (e.g. 'CONFIRMAR NOTA 8.50').")] string confirmationText,
        [Description("Moodle connection alias.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(confirmationText))
            return ToolResultHelper.Error<IndividualGradeSendResult>("Confirmation text is required.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<IndividualGradeSendResult>("User not authenticated.");

        IndividualGradeSendResult result;
        try
        {
            result = await mediator.Send(
                new ConfirmIndividualGradeCommand(pendingActionId, confirmationText),
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<IndividualGradeSendResult>(ex.Message);
        }

        var isError = result.Status is "failed" or "rejected";
        var response = new ToolResponse<IndividualGradeSendResult>(result.Status, result, [], result.AuditId, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = result.Status switch
                {
                    "launched" => $"Grade {result.LaunchedGrade:F2} successfully submitted for student {result.StudentId} on assignment {result.AssignmentId}. AuditId: {result.AuditId}.",
                    "rejected" => $"Confirmation rejected: {string.Join("; ", result.Warnings)}",
                    _ => $"Grade launch failed: {string.Join("; ", result.Warnings)}"
                }
            }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = isError
        };
    }
}
