using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

public interface IGradingReviewRepository
{
    Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken);

    Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken);

    Task<AssistedGradingBatch?> GetBatchByIdempotencyKeyAsync(
        string createdBySubject,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        Task.FromResult<AssistedGradingBatch?>(null);

    Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken);

    Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken);

    Task UpdateArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Este repositorio nao suporta atualizacao de artifacts.");

    Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken);

    Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<Guid, AssistedGradingItem>> GetItemsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, AssistedGradingItem>();
        foreach (var id in ids)
        {
            var item = await GetItemAsync(id, cancellationToken);
            if (item is not null) result[id] = item;
        }
        return result;
    }

    Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
        Guid batchId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<GradingArtifact>>> ListArtifactsByItemsAsync(
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<GradingArtifact>>();
        foreach (var id in gradingItemIds)
        {
            result[id] = await ListArtifactsByItemAsync(id, cancellationToken);
        }

        return result;
    }

    Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<GradingEvidence>>> ListEvidenceByItemsAsync(
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<GradingEvidence>>();
        foreach (var id in gradingItemIds)
        {
            result[id] = await ListEvidenceByItemAsync(id, cancellationToken);
        }

        return result;
    }

    Task<IReadOnlyDictionary<Guid, GradingContextSnapshotDocument>> ListLatestContextSnapshotsByItemsAsync(
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, GradingContextSnapshotDocument>>(
            new Dictionary<Guid, GradingContextSnapshotDocument>());

    Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
        GradingBatchStatus status,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(
        string createdBySubject,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
