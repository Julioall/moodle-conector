using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

public interface IGradingReviewRepository
{
    Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken);

    Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken);

    Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken);

    Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken);

    Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken);

    Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
        Guid batchId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
