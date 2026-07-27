using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

public sealed class PendingMoodleActionRepository(ConnectorDbContext dbContext) : IPendingMoodleActionRepository
{
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = await dbContext.PendingMoodleActions
            .Where(action => action.Id == id &&
                             action.Status == PendingActionStatus.PendingConfirmation &&
                             action.ExpiresAt > confirmedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(action => action.Status, PendingActionStatus.Confirmed)
                .SetProperty(action => action.ConfirmedBySubject, confirmedBySubject)
                .SetProperty(action => action.ConfirmedAt, confirmedAt), cancellationToken);

        if (updated == 1)
        {
            await dbContext.MoodleAuditLogs.AddAsync(confirmationAudit, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PendingActionConfirmationClaimResult(true, PendingActionStatus.Confirmed, confirmedAt);
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

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
