using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Infrastructure;

public sealed class GradingReviewRepository(ConnectorDbContext dbContext) : IGradingReviewRepository
{
    public async Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken)
    {
        await dbContext.GradingBatches.AddAsync(batch, cancellationToken);
    }

    public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.GradingBatches.SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);
    }

    public async Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken)
    {
        await dbContext.GradingItems.AddAsync(item, cancellationToken);
    }

    public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.GradingItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
        Guid batchId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        return await dbContext.GradingItems
            .Where(item => item.BatchId == batchId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToArrayAsync(cancellationToken);
    }

    public Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        return dbContext.GradingItems.CountAsync(item => item.BatchId == batchId, cancellationToken);
    }

    public async Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken)
    {
        return await dbContext.GradingArtifacts
            .Where(artifact => artifact.GradingItemId == gradingItemId)
            .OrderBy(artifact => artifact.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
