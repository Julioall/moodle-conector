using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

public sealed class PendingMoodleActionRepository(ConnectorDbContext dbContext) : IPendingMoodleActionRepository
{
    private bool IsInMemory => dbContext.Database.ProviderName?.Contains(
        "InMemory",
        StringComparison.OrdinalIgnoreCase) == true;

    public async Task AddAsync(PendingMoodleAction action, CancellationToken cancellationToken)
    {
        await dbContext.PendingMoodleActions.AddAsync(action, cancellationToken);
    }

    public Task<PendingMoodleAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.PendingMoodleActions.SingleOrDefaultAsync(action => action.Id == id, cancellationToken);
    }

    public async Task<PendingActionConfirmationClaimResult> TryConfirmWithAuditAsync(
        Guid id,
        string confirmedBySubject,
        DateTimeOffset confirmedAt,
        MoodleAuditLog confirmationAudit,
        CancellationToken cancellationToken)
    {
        return await TryConfirmCoreAsync(
            id,
            confirmedBySubject,
            confirmedAt,
            confirmationAudit,
            PendingActionStatus.Confirmed,
            cancellationToken);
    }

    public async Task<PendingActionConfirmationClaimResult> TryAuthorizeWithAuditAsync(
        Guid id,
        string confirmedBySubject,
        DateTimeOffset confirmedAt,
        MoodleAuditLog confirmationAudit,
        CancellationToken cancellationToken)
    {
        return await TryConfirmCoreAsync(
            id,
            confirmedBySubject,
            confirmedAt,
            confirmationAudit,
            PendingActionStatus.Authorized,
            cancellationToken);
    }

    private async Task<PendingActionConfirmationClaimResult> TryConfirmCoreAsync(
        Guid id,
        string confirmedBySubject,
        DateTimeOffset confirmedAt,
        MoodleAuditLog confirmationAudit,
        PendingActionStatus targetStatus,
        CancellationToken cancellationToken)
    {
        if (IsInMemory)
        {
            var inMemoryAction = await dbContext.PendingMoodleActions
                .SingleOrDefaultAsync(action => action.Id == id, cancellationToken);
            if (inMemoryAction is null)
            {
                throw new InvalidOperationException("Acao pendente nao encontrada.");
            }

            var canTransition =
                (inMemoryAction.Status == PendingActionStatus.PendingConfirmation &&
                 inMemoryAction.ExpiresAt > confirmedAt) ||
                (targetStatus == PendingActionStatus.Authorized &&
                 inMemoryAction.Status == PendingActionStatus.PartiallyCompleted);
            if (canTransition)
            {
                if (targetStatus == PendingActionStatus.Authorized)
                {
                    inMemoryAction.Authorize(confirmedBySubject, confirmedAt);
                }
                else
                {
                    inMemoryAction.Confirm(confirmedBySubject, confirmedAt);
                }

                await dbContext.MoodleAuditLogs.AddAsync(confirmationAudit, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return new PendingActionConfirmationClaimResult(true, targetStatus, confirmedAt);
            }

            if (inMemoryAction.Status == PendingActionStatus.PendingConfirmation &&
                inMemoryAction.ExpiresAt <= confirmedAt)
            {
                inMemoryAction.MarkExpired();
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return new PendingActionConfirmationClaimResult(
                false,
                inMemoryAction.Status,
                inMemoryAction.ConfirmedAt);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = await dbContext.PendingMoodleActions
            .Where(action => action.Id == id &&
                             ((action.Status == PendingActionStatus.PendingConfirmation &&
                               action.ExpiresAt > confirmedAt) ||
                              // A partial durable publication is terminal for
                              // its previous attempt, but an explicit repeat
                              // confirmation is allowed to requeue the
                              // unresolved items after the original preview
                              // TTL has elapsed.
                              (targetStatus == PendingActionStatus.Authorized &&
                               action.Status == PendingActionStatus.PartiallyCompleted)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(action => action.Status, targetStatus)
                .SetProperty(action => action.ConfirmedBySubject, confirmedBySubject)
                .SetProperty(action => action.ConfirmedAt, confirmedAt), cancellationToken);

        if (updated == 1)
        {
            await dbContext.MoodleAuditLogs.AddAsync(confirmationAudit, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await RefreshTrackedActionAsync(id, cancellationToken);
            return new PendingActionConfirmationClaimResult(true, targetStatus, confirmedAt);
        }

        await dbContext.PendingMoodleActions
            .Where(action => action.Id == id &&
                             action.Status == PendingActionStatus.PendingConfirmation &&
                             action.ExpiresAt <= confirmedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(action => action.Status, PendingActionStatus.Expired), cancellationToken);

        var current = await dbContext.PendingMoodleActions
            .AsNoTracking()
            .SingleAsync(action => action.Id == id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PendingActionConfirmationClaimResult(false, current.Status, current.ConfirmedAt);
    }

    public async Task<PendingActionExecutionClaimResult> TryBeginExecutionAsync(
        Guid id,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(workerId) || leaseDuration <= TimeSpan.Zero)
        {
            return new PendingActionExecutionClaimResult(false, PendingActionStatus.Failed, null, 0);
        }

        var normalizedWorkerId = workerId.Trim();
        var leaseUntil = now.Add(leaseDuration);
        var current = await dbContext.PendingMoodleActions
            .AsNoTracking()
            .SingleOrDefaultAsync(action => action.Id == id, cancellationToken);
        if (current is null)
        {
            return new PendingActionExecutionClaimResult(false, PendingActionStatus.Failed, null, 0);
        }

        if (current.Status is PendingActionStatus.Executed or PendingActionStatus.Failed or PendingActionStatus.ExecutionUnknown)
        {
            return new PendingActionExecutionClaimResult(false, current.Status, current.ExecutionLeaseUntil, current.ExecutionAttemptCount);
        }

        var updated = await dbContext.PendingMoodleActions
            .Where(action => action.Id == id &&
                (action.Status == PendingActionStatus.Confirmed ||
                 action.Status == PendingActionStatus.Authorized ||
                 action.Status == PendingActionStatus.PartiallyCompleted ||
                 (action.Status == PendingActionStatus.Executing &&
                  (action.ExecutionLeaseUntil == null || action.ExecutionLeaseUntil <= now || action.ExecutionOwner == normalizedWorkerId))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(action => action.Status, PendingActionStatus.Executing)
                .SetProperty(action => action.ExecutionOwner, normalizedWorkerId)
                .SetProperty(action => action.ExecutionLeaseUntil, leaseUntil)
                .SetProperty(action => action.ExecutionAttemptCount, action => action.ExecutionAttemptCount + 1)
                .SetProperty(action => action.LastExecutionError, (string?)null), cancellationToken);

        if (updated != 1)
        {
            var observed = await dbContext.PendingMoodleActions
                .AsNoTracking()
                .SingleAsync(action => action.Id == id, cancellationToken);
            return new PendingActionExecutionClaimResult(false, observed.Status, observed.ExecutionLeaseUntil, observed.ExecutionAttemptCount);
        }

        await RefreshTrackedActionAsync(id, cancellationToken);
        return new PendingActionExecutionClaimResult(true, PendingActionStatus.Executing, leaseUntil, current.ExecutionAttemptCount + 1);
    }

    public async Task<IReadOnlyList<PendingMoodleAction>> ListRecoverableGradingPublicationsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        return await dbContext.PendingMoodleActions
            .AsNoTracking()
            .Where(action =>
                (action.ToolName == "criar_previa_lancamento_lote" ||
                 action.ToolName == "confirmar_lancamento_lote_moodle") &&
                // Confirmed is retained for actions created before the
                // Authorized state was introduced. They must still be
                // recoverable after a process restart; new public calls use
                // Authorized and never rely on this compatibility branch.
                (action.Status == PendingActionStatus.Confirmed ||
                 action.Status == PendingActionStatus.Authorized ||
                 (action.Status == PendingActionStatus.Executing &&
                  (action.ExecutionLeaseUntil == null || action.ExecutionLeaseUntil <= now))))
            .OrderBy(action => action.CreatedAt)
            .ThenBy(action => action.Id)
            .Take(safeLimit)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListTerminalGradingPublicationIdsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 1000);
        var payloads = await dbContext.PendingMoodleActions
            .AsNoTracking()
            .Where(action =>
                (action.ToolName == "criar_previa_lancamento_lote" ||
                 action.ToolName == "confirmar_lancamento_lote_moodle") &&
                (action.Status == PendingActionStatus.Executed ||
                 action.Status == PendingActionStatus.Failed ||
                 action.Status == PendingActionStatus.PartiallyCompleted))
            .OrderByDescending(action => action.CreatedAt)
            .Take(safeLimit)
            .Select(action => action.PayloadJson)
            .ToArrayAsync(cancellationToken);

        var publicationIds = new HashSet<Guid>();
        foreach (var payloadJson in payloads)
        {
            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                var root = document.RootElement;
                if ((!root.TryGetProperty("publicationId", out var publicationIdElement) &&
                     !root.TryGetProperty("PublicationId", out publicationIdElement)) ||
                    publicationIdElement.ValueKind != JsonValueKind.String ||
                    !publicationIdElement.TryGetGuid(out var publicationId) ||
                    publicationId == Guid.Empty)
                {
                    continue;
                }

                publicationIds.Add(publicationId);
            }
            catch (JsonException)
            {
                // An invalid historical payload cannot be used to resolve a
                // claim; leave it for manual audit instead of failing the
                // whole publication recovery sweep.
            }
        }

        return publicationIds.ToArray();
    }

    public async Task<bool> TryRenewExecutionLeaseAsync(
        Guid id,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(workerId) || leaseDuration <= TimeSpan.Zero)
        {
            return false;
        }

        var normalizedWorkerId = workerId.Trim();
        var updated = await dbContext.PendingMoodleActions
            .Where(action => action.Id == id &&
                             action.Status == PendingActionStatus.Executing &&
                             action.ExecutionOwner == normalizedWorkerId &&
                             action.ExecutionLeaseUntil > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                action => action.ExecutionLeaseUntil,
                now.Add(leaseDuration)), cancellationToken);
        return updated == 1;
    }

    public async Task<PendingActionReconciliationClaimResult> TryResolveExecutionUnknownWithAuditAsync(
        Guid id,
        PendingActionStatus resolvedStatus,
        MoodleAuditLog reconciliationAudit,
        CancellationToken cancellationToken)
    {
        if (resolvedStatus is not (PendingActionStatus.Executed or PendingActionStatus.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedStatus));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = await dbContext.PendingMoodleActions
            .Where(action => action.Id == id && action.Status == PendingActionStatus.ExecutionUnknown)
            .ExecuteUpdateAsync(setters => setters.SetProperty(action => action.Status, resolvedStatus), cancellationToken);

        if (updated == 1)
        {
            await dbContext.MoodleAuditLogs.AddAsync(reconciliationAudit, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PendingActionReconciliationClaimResult(true, resolvedStatus, reconciliationAudit.Id.ToString("N"));
        }

        var current = await dbContext.PendingMoodleActions
            .AsNoTracking()
            .SingleAsync(action => action.Id == id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PendingActionReconciliationClaimResult(false, current.Status, null);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshTrackedActionAsync(Guid id, CancellationToken cancellationToken)
    {
        var tracked = dbContext.PendingMoodleActions.Local.SingleOrDefault(action => action.Id == id);
        if (tracked is not null)
        {
            await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
        }
    }
}
