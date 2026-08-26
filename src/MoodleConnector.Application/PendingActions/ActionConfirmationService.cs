using System.Text.Json;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.PendingActions;

public sealed class ActionConfirmationService(
    IPendingMoodleActionRepository pendingActions,
    ICurrentUserContext currentUser,
    IMoodleUserResolver moodleUserResolver,
    IAuthorizationAuditService authorizationAudit) : IActionConfirmationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ActionConfirmationResponse> ConfirmAsync(
        Guid pendingActionId,
        string confirmationText,
        string? requiredScope,
        CancellationToken cancellationToken)
    {
        var action = await pendingActions.GetByIdAsync(pendingActionId, cancellationToken)
            ?? throw new InvalidOperationException("Acao pendente nao encontrada.");

        if (!string.Equals(action.CreatedBySubject, currentUser.Subject, StringComparison.Ordinal) &&
            !currentUser.HasPlatformPermission("tool.pending_actions.manage"))
        {
            await RecordAuthorizationFailureAsync(
                "pending_action_actor_mismatch",
                "Apenas o criador da acao ou um administrador Moodle pode confirma-la.",
                action,
                cancellationToken);
            throw new InvalidOperationException("Apenas o criador da acao ou um administrador Moodle pode confirma-la.");
        }

        if (!string.IsNullOrWhiteSpace(requiredScope) && !currentUser.HasScope(requiredScope))
        {
            await RecordAuthorizationFailureAsync(
                "missing_required_scope",
                $"Escopo obrigatorio ausente: {requiredScope}.",
                action,
                cancellationToken);
            throw new InvalidOperationException($"Escopo obrigatorio ausente: {requiredScope}.");
        }

        if (!string.Equals(action.ConfirmationText, confirmationText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Texto de confirmacao invalido.");
        }

        if (action.Status == PendingActionStatus.Confirmed)
        {
            return new ActionConfirmationResponse(
                "already_confirmed",
                action.Id,
                action.ToolName,
                action.RiskLevel,
                action.ConfirmedAt ?? DateTimeOffset.UtcNow,
                action.CorrelationId);
        }

        if (action.Status == PendingActionStatus.ExecutionUnknown)
        {
            throw new InvalidOperationException(
                "O resultado remoto desta acao e desconhecido. Reconcilie a acao antes de tentar qualquer nova execucao.");
        }

        if (action.Status != PendingActionStatus.PendingConfirmation)
        {
            throw new InvalidOperationException($"A acao nao pode ser confirmada no estado {action.Status}.");
        }

        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var audit = new MoodleAuditLog
        {
            CorrelationId = action.CorrelationId,
            ToolName = action.ToolName,
            RiskLevel = action.RiskLevel,
            ActorSubject = currentUser.Subject,
            ActorEmail = currentUser.Email,
            ActorMoodleUserId = moodleUserId,
            CourseId = action.CourseId,
            RequestSanitizedJson = AuditPayloadSanitizer.SanitizeJson(action.PreviewJson),
            ResponseSummaryJson = JsonSerializer.Serialize(new { action.Id, confirmedAt = now }, JsonOptions),
            Status = "confirmed"
        };
        var claim = await pendingActions.TryConfirmWithAuditAsync(
            action.Id,
            currentUser.Subject,
            now,
            audit,
            cancellationToken);
        if (!claim.ConfirmedByCaller)
        {
            if (claim.Status == PendingActionStatus.Confirmed)
            {
                return new ActionConfirmationResponse(
                    "already_confirmed",
                    action.Id,
                    action.ToolName,
                    action.RiskLevel,
                    claim.ConfirmedAt ?? now,
                    action.CorrelationId);
            }

            if (claim.Status == PendingActionStatus.Expired)
            {
                throw new InvalidOperationException("A acao pendente expirou.");
            }

            throw new InvalidOperationException($"A acao nao pode ser confirmada no estado {claim.Status}.");
        }

        return new ActionConfirmationResponse(
            "confirmed",
            action.Id,
            action.ToolName,
            action.RiskLevel,
            now,
            action.CorrelationId);
    }

    private Task RecordAuthorizationFailureAsync(
        string reason,
        string message,
        PendingMoodleAction action,
        CancellationToken cancellationToken)
    {
        return authorizationAudit.RecordFailureAsync(new AuthorizationFailureAuditRequest(
            "pending_action_confirmation",
            reason,
            message,
            currentUser.Subject,
            currentUser.Email,
            Path: null,
            AuthenticationType: null,
            action.CorrelationId), cancellationToken);
    }
}
