using System.Text.Json;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleWriteReconciliationService(
    IPendingMoodleActionRepository pendingActions,
    ICurrentUserContext currentUser,
    IGradingReviewRepository? gradingRepository = null) : IMoodleWriteReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MoodleWriteReconciliationResult> ReconcileAsync(
        Guid pendingActionId,
        string resolution,
        CancellationToken cancellationToken)
    {
        var action = await pendingActions.GetByIdAsync(pendingActionId, cancellationToken)
            ?? throw new InvalidOperationException("Ação pendente não encontrada.");

        if (!string.Equals(action.CreatedBySubject, currentUser.Subject, StringComparison.Ordinal) &&
            !currentUser.HasPlatformPermission("tool.pending_actions.manage"))
        {
            throw new InvalidOperationException("Apenas o criador da ação ou um administrador pode reconciliá-la.");
        }

        var isGradingBatch = action.ToolName is "criar_previa_lancamento_lote" or "confirmar_lancamento_lote_moodle";
        if (action.Status is PendingActionStatus.Executed or PendingActionStatus.Failed)
        {
            // The action row and grading ledger are persisted separately. A
            // crash after the atomic action resolution but before the item
            // statuses/checkpoint is possible; repair those unknown items on
            // the next reconciliation request instead of leaving them stuck
            // forever behind a terminal action.
            if (isGradingBatch && gradingRepository is not null)
            {
                await RepairTerminalGradingItemsAsync(
                    action,
                    action.Status == PendingActionStatus.Executed,
                    cancellationToken);
            }

            throw new InvalidOperationException(
                $"A ação só pode ser reconciliada no estado ExecutionUnknown; estado atual: {action.Status}.");
        }

        if (action.Status != PendingActionStatus.ExecutionUnknown)
        {
            throw new InvalidOperationException($"A ação só pode ser reconciliada no estado ExecutionUnknown; estado atual: {action.Status}.");
        }

        using var payloadDocument = JsonDocument.Parse(action.PayloadJson);
        var functionName = payloadDocument.RootElement.TryGetProperty("function", out var function)
            ? function.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(functionName) && isGradingBatch)
        {
            functionName = "mod_assign_save_grade";
        }
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new InvalidOperationException("A ação não contém a função Moodle necessária para reconciliação.");
        }

        var scope = MoodleWriteScopePolicy.ForFunction(functionName);
        if (!currentUser.HasScope(scope))
        {
            throw new InvalidOperationException($"Escopo obrigatório ausente: {scope}.");
        }

        var normalizedResolution = resolution.Trim().ToLowerInvariant() switch
        {
            "executed" or "applied" or "aplicada" => (PendingActionStatus.Executed, "executed"),
            "not_applied" or "not-applied" or "failed" or "nao_aplicada" or "não_aplicada" => (PendingActionStatus.Failed, "not_applied"),
            _ => throw new ArgumentException("A resolução deve ser 'executed' ou 'not_applied'.", nameof(resolution))
        };

        var now = DateTimeOffset.UtcNow;
        var audit = new MoodleAuditLog
        {
            CorrelationId = action.CorrelationId,
            ToolName = "moodle_reconcile_write",
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = currentUser.Subject,
            ActorEmail = currentUser.Email,
            ActorMoodleUserId = action.CreatedByMoodleUserId,
            CourseId = action.CourseId,
            MoodleFunction = functionName,
            PendingActionId = action.Id,
            StartedAt = now,
            FinishedAt = now,
            DurationMs = 0,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                actionId = action.Id,
                function = functionName,
                resolution = normalizedResolution.Item2
            }),
            ResponseSummaryJson = JsonSerializer.Serialize(new { resolution = normalizedResolution.Item2 }, JsonOptions),
            Status = normalizedResolution.Item2 == "executed"
                ? "write_reconciled_executed"
                : "write_reconciled_not_applied"
        };

        var claim = await pendingActions.TryResolveExecutionUnknownWithAuditAsync(
            action.Id,
            normalizedResolution.Item1,
            audit,
            cancellationToken);
        if (!claim.ResolvedByCaller)
        {
            if (claim.Status == PendingActionStatus.ExecutionUnknown)
            {
                throw new InvalidOperationException("A reconciliação concorrente não pôde ser concluída; tente novamente.");
            }

            return new MoodleWriteReconciliationResult(
                "already_reconciled",
                action.Id,
                functionName,
                claim.Status == PendingActionStatus.Executed ? "executed" : "not_applied",
                claim.AuditId,
                "A ação já havia sido reconciliada e não foi alterada novamente.");
        }

        if (isGradingBatch && gradingRepository is not null &&
            payloadDocument.RootElement.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            var applied = normalizedResolution.Item1 == PendingActionStatus.Executed;
            var gradingItemIds = items.EnumerateArray()
                .Where(itemElement =>
                    itemElement.TryGetProperty("gradingItemId", out var itemIdElement) &&
                    itemIdElement.TryGetGuid(out var gradingItemId) &&
                    gradingItemId != Guid.Empty)
                .Select(itemElement => itemElement.GetProperty("gradingItemId").GetGuid())
                .Distinct()
                .ToArray();
            // Reconciliation can cover the whole aggregate. Hydrate the
            // ledger in one indexed query instead of issuing one SELECT per
            // item (which made a 10k action an N+1 recovery operation).
            var gradingItems = await gradingRepository.GetItemsAsync(
                gradingItemIds,
                cancellationToken);
            foreach (var gradingItem in gradingItems.Values)
            {
                if (gradingItem?.CommitStatus == GradingCommitStatus.ExecutionUnknown)
                {
                    gradingItem.ResolveCommitExecutionUnknown(applied);
                }
            }
            await gradingRepository.SaveChangesAsync(cancellationToken);
            if (payloadDocument.RootElement.TryGetProperty("publicationId", out var publicationIdElement) &&
                publicationIdElement.TryGetGuid(out var publicationId))
            {
                await gradingRepository.ReleasePublicationClaimsAsync(publicationId, cancellationToken);
            }
        }

        return new MoodleWriteReconciliationResult(
            "reconciled",
            action.Id,
            functionName,
            normalizedResolution.Item2,
            claim.AuditId,
            normalizedResolution.Item1 == PendingActionStatus.Executed
                ? "A operação foi marcada como aplicada no Moodle; nenhuma nova requisição foi enviada."
                : "A operação foi marcada como não aplicada; nenhuma nova requisição foi enviada. Crie uma nova prévia se necessário.");
    }

    private async Task RepairTerminalGradingItemsAsync(
        PendingMoodleAction action,
        bool applied,
        CancellationToken cancellationToken)
    {
        if (gradingRepository is null)
        {
            return;
        }

        try
        {
            using var payloadDocument = JsonDocument.Parse(action.PayloadJson);
            if (!payloadDocument.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var gradingItemIds = items.EnumerateArray()
                .Where(itemElement =>
                    itemElement.TryGetProperty("gradingItemId", out var itemIdElement) &&
                    itemIdElement.TryGetGuid(out var gradingItemId) &&
                    gradingItemId != Guid.Empty)
                .Select(itemElement => itemElement.GetProperty("gradingItemId").GetGuid())
                .Distinct()
                .ToArray();
            var gradingItems = await gradingRepository.GetItemsAsync(
                gradingItemIds,
                cancellationToken);
            foreach (var gradingItem in gradingItems.Values)
            {
                if (gradingItem.CommitStatus == GradingCommitStatus.ExecutionUnknown)
                {
                    gradingItem.ResolveCommitExecutionUnknown(applied);
                }
            }

            await gradingRepository.SaveChangesAsync(cancellationToken);
            if (payloadDocument.RootElement.TryGetProperty("publicationId", out var publicationIdElement) &&
                publicationIdElement.TryGetGuid(out var publicationId))
            {
                await gradingRepository.ReleasePublicationClaimsAsync(publicationId, cancellationToken);
            }
        }
        catch (JsonException)
        {
            // Invalid historical payloads stay available for manual audit;
            // never fail an unrelated reconciliation repair sweep.
        }
    }
}
