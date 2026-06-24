using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Messages;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Messages;

/// <summary>
/// Tools para o ciclo de mensagens tipificadas do tutor SENAI CTM.
/// Cada tipo de mensagem tem um par preparar/confirmar, seguindo o padrão PendingAction.
/// </summary>
[McpServerToolType]
public sealed class MoodleTutorMessageTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    // ── Boas-vindas ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "preparar_mensagem_boas_vindas", Title = "Preparar Mensagem Boas Vindas",
        ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessagePreview>))]
    [Description("Prepara uma mensagem de boas-vindas para os destinatários informados. Retorna prévia e pendingActionId para confirmação. Use ao iniciar um novo curso.")]
    public Task<CallToolResult> PrepararMensagemBoasVindasAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Lista de IDs Moodle dos destinatários.")] IReadOnlyList<string> recipientIds,
        [Description("Texto personalizado (opcional, substitui o modelo padrão).")] string? customText = null,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => PrepareMsgCoreAsync(courseId, TutorMessageType.BoasVindas, recipientIds, customText, moodleAlias, cancellationToken);

    [McpServerTool(Name = "confirmar_mensagem_boas_vindas", Title = "Confirmar Mensagem Boas Vindas",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessageSendResult>))]
    [Description("Confirma e envia a mensagem de boas-vindas previamente preparada.")]
    public Task<CallToolResult> ConfirmarMensagemBoasVindasAsync(
        [Description("ID da ação pendente retornado por preparar_mensagem_boas_vindas.")] Guid pendingActionId,
        [Description("Texto de confirmação exato conforme indicado na prévia.")] string confirmationText,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ConfirmMsgCoreAsync(pendingActionId, confirmationText, moodleAlias, cancellationToken);

    // ── Cobrança de acesso ───────────────────────────────────────────────────

    [McpServerTool(Name = "preparar_mensagem_cobranca_acesso", Title = "Preparar Mensagem Cobranca Acesso",
        ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessagePreview>))]
    [Description("Prepara mensagem de cobrança para estudantes que não acessaram o AVA. Use após listar_alunos_sem_acesso para direcionar os destinatários.")]
    public Task<CallToolResult> PrepararMensagemCobrancaAcessoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Lista de IDs Moodle dos destinatários (obtidos de listar_alunos_sem_acesso).")] IReadOnlyList<string> recipientIds,
        [Description("Texto personalizado (opcional).")] string? customText = null,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => PrepareMsgCoreAsync(courseId, TutorMessageType.CobrancaAcesso, recipientIds, customText, moodleAlias, cancellationToken);

    [McpServerTool(Name = "confirmar_mensagem_cobranca_acesso", Title = "Confirmar Mensagem Cobranca Acesso",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessageSendResult>))]
    [Description("Confirma e envia a mensagem de cobrança de acesso.")]
    public Task<CallToolResult> ConfirmarMensagemCobrancaAcessoAsync(
        [Description("ID da ação pendente retornado por preparar_mensagem_cobranca_acesso.")] Guid pendingActionId,
        [Description("Texto de confirmação exato conforme indicado na prévia.")] string confirmationText,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ConfirmMsgCoreAsync(pendingActionId, confirmationText, moodleAlias, cancellationToken);

    // ── Cobrança de SA ───────────────────────────────────────────────────────

    [McpServerTool(Name = "preparar_mensagem_cobranca_sa", Title = "Preparar Mensagem Cobranca SA",
        ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessagePreview>))]
    [Description("Prepara mensagem de cobrança para estudantes com SA pendente de entrega. Use após listar_alunos_pendentes_atividade para direcionar os destinatários.")]
    public Task<CallToolResult> PrepararMensagemCobrancaSaAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Lista de IDs Moodle dos destinatários (obtidos de listar_alunos_pendentes_atividade).")] IReadOnlyList<string> recipientIds,
        [Description("Texto personalizado (opcional).")] string? customText = null,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => PrepareMsgCoreAsync(courseId, TutorMessageType.CobrancaSa, recipientIds, customText, moodleAlias, cancellationToken);

    [McpServerTool(Name = "confirmar_mensagem_cobranca_sa", Title = "Confirmar Mensagem Cobranca SA",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessageSendResult>))]
    [Description("Confirma e envia a mensagem de cobrança de SA.")]
    public Task<CallToolResult> ConfirmarMensagemCobrancaSaAsync(
        [Description("ID da ação pendente retornado por preparar_mensagem_cobranca_sa.")] Guid pendingActionId,
        [Description("Texto de confirmação exato conforme indicado na prévia.")] string confirmationText,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ConfirmMsgCoreAsync(pendingActionId, confirmationText, moodleAlias, cancellationToken);

    // ── Recuperação ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "preparar_mensagem_recuperacao", Title = "Preparar Mensagem Recuperacao",
        ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessagePreview>))]
    [Description("Prepara mensagem de recuperação paralela para estudantes com conceito abaixo do mínimo. Use após listar_alunos_abaixo_minimo. ATENÇÃO: garanta que a atividade de recuperação esteja publicada antes de confirmar o envio.")]
    public Task<CallToolResult> PrepararMensagemRecuperacaoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Lista de IDs Moodle dos destinatários (obtidos de listar_alunos_abaixo_minimo).")] IReadOnlyList<string> recipientIds,
        [Description("Texto personalizado (opcional).")] string? customText = null,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => PrepareMsgCoreAsync(courseId, TutorMessageType.Recuperacao, recipientIds, customText, moodleAlias, cancellationToken);

    [McpServerTool(Name = "confirmar_mensagem_recuperacao", Title = "Confirmar Mensagem Recuperacao",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessageSendResult>))]
    [Description("Confirma e envia a mensagem de recuperação paralela.")]
    public Task<CallToolResult> ConfirmarMensagemRecuperacaoAsync(
        [Description("ID da ação pendente retornado por preparar_mensagem_recuperacao.")] Guid pendingActionId,
        [Description("Texto de confirmação exato conforme indicado na prévia.")] string confirmationText,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ConfirmMsgCoreAsync(pendingActionId, confirmationText, moodleAlias, cancellationToken);

    // ── Encerramento ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "preparar_mensagem_encerramento", Title = "Preparar Mensagem Encerramento",
        ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessagePreview>))]
    [Description("Prepara aviso de encerramento de prazo de fórum ou SA.")]
    public Task<CallToolResult> PrepararMensagemEncerramentoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Lista de IDs Moodle dos destinatários.")] IReadOnlyList<string> recipientIds,
        [Description("Texto personalizado (opcional).")] string? customText = null,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => PrepareMsgCoreAsync(courseId, TutorMessageType.Encerramento, recipientIds, customText, moodleAlias, cancellationToken);

    [McpServerTool(Name = "confirmar_mensagem_encerramento", Title = "Confirmar Mensagem Encerramento",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessageSendResult>))]
    [Description("Confirma e envia a mensagem de encerramento de prazo.")]
    public Task<CallToolResult> ConfirmarMensagemEncerramentoAsync(
        [Description("ID da ação pendente retornado por preparar_mensagem_encerramento.")] Guid pendingActionId,
        [Description("Texto de confirmação exato conforme indicado na prévia.")] string confirmationText,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ConfirmMsgCoreAsync(pendingActionId, confirmationText, moodleAlias, cancellationToken);

    // ── Acompanhamento ───────────────────────────────────────────────────────

    [McpServerTool(Name = "preparar_mensagem_acompanhamento", Title = "Preparar Mensagem Acompanhamento",
        ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessagePreview>))]
    [Description("Prepara mensagem de incentivo/acompanhamento geral para estudantes selecionados.")]
    public Task<CallToolResult> PrepararMensagemAcompanhamentoAsync(
        [Description("Identificador do curso Moodle.")] string courseId,
        [Description("Lista de IDs Moodle dos destinatários.")] IReadOnlyList<string> recipientIds,
        [Description("Texto personalizado (opcional).")] string? customText = null,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => PrepareMsgCoreAsync(courseId, TutorMessageType.Acompanhamento, recipientIds, customText, moodleAlias, cancellationToken);

    [McpServerTool(Name = "confirmar_mensagem_acompanhamento", Title = "Confirmar Mensagem Acompanhamento",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<TutorMessageSendResult>))]
    [Description("Confirma e envia a mensagem de acompanhamento.")]
    public Task<CallToolResult> ConfirmarMensagemAcompanhamentoAsync(
        [Description("ID da ação pendente retornado por preparar_mensagem_acompanhamento.")] Guid pendingActionId,
        [Description("Texto de confirmação exato conforme indicado na prévia.")] string confirmationText,
        [Description("Alias do Moodle a usar.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
        => ConfirmMsgCoreAsync(pendingActionId, confirmationText, moodleAlias, cancellationToken);

    // ── Core helpers ─────────────────────────────────────────────────────────

    private async Task<CallToolResult> PrepareMsgCoreAsync(
        string courseId,
        TutorMessageType messageType,
        IReadOnlyList<string> recipientIds,
        string? customText,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<TutorMessagePreview>("Informe um identificador de curso válido.");

        if (recipientIds == null || recipientIds.Count == 0)
            return ToolResultHelper.Error<TutorMessagePreview>("Informe pelo menos um destinatário.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<TutorMessagePreview>("Usuário não autenticado.");

        TutorMessagePreview preview;
        try
        {
            preview = await mediator.Send(
                new PrepareTutorMessageCommand(courseId, messageType, recipientIds, customText),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException ex)
        {
            return ToolResultHelper.Error<TutorMessagePreview>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<TutorMessagePreview>("Não foi possível preparar a mensagem neste momento.");
        }

        var response = new ToolResponse<TutorMessagePreview>("ok", preview, [], AuditId: null, DateTimeOffset.UtcNow);
        var narration = $"Mensagem {messageType} preparada para {preview.RecipientCount} destinatário(s). " +
                        $"Revise o texto e confirme usando o texto: '{preview.ConfirmationText}'. " +
                        $"Expira em: {preview.ExpiresAt:HH:mm}.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> ConfirmMsgCoreAsync(
        Guid pendingActionId,
        string confirmationText,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<TutorMessageSendResult>("Usuário não autenticado.");

        TutorMessageSendResult result;
        try
        {
            result = await mediator.Send(
                new ConfirmTutorMessageCommand(pendingActionId, confirmationText),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<TutorMessageSendResult>("Não foi possível confirmar o envio da mensagem neste momento.");
        }

        var response = new ToolResponse<TutorMessageSendResult>(result.Status, result, [], AuditId: result.AuditId, DateTimeOffset.UtcNow);
        var narration = result.Status switch
        {
            "sent" => $"Mensagem {result.MessageType} enviada com sucesso para {result.SentCount} destinatário(s).",
            "partial" => $"Mensagem {result.MessageType} enviada parcialmente: {result.SentCount} enviada(s), {result.FailedCount} falha(s).",
            "already_confirmed" => "Esta mensagem já havia sido enviada anteriormente.",
            _ => $"Envio com status '{result.Status}': {result.SentCount} enviada(s), {result.FailedCount} falha(s)."
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = result.Status == "failed" || result.Status == "error" || result.Status == "not_found"
        };
    }
}
