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

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
