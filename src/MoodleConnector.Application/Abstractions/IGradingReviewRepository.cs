using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

public sealed record GradingPublicationClaimRequest(
    Guid GradingItemId,
    long AssignmentId,
    long MoodleUserId,
    int AttemptNumber);

public sealed record GradingPublicationClaimResult(
    Guid GradingItemId,
    bool Claimed,
    string? ConflictCode = null);

public sealed record GradingRunScope(
    Guid GradingRunId,
    string CreatedBySubject,
    long? CreatedByMoodleUserId,
    string? MoodleConnectionId,
    string? ConnectorClientId,
    string? ConnectionAlias,
    string? CourseIdScope,
    string Destination,
    GradingRunStatus Status);

public interface IGradingReviewRepository
{
    Task AddGradingRunAsync(GradingRun run, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Este repositorio nao suporta execucoes agregadoras.");

    /// <summary>
    /// Atomically chooses the run destination. A concurrent CSV/publication
    /// request can only win if the run is still undecided; selecting the same
    /// destination remains idempotent.
    /// </summary>
    Task<bool> TrySetGradingRunDestinationAsync(
        Guid gradingRunId,
        string destination,
        CancellationToken cancellationToken)
    {
        var runTask = GetGradingRunAsync(gradingRunId, cancellationToken);
        return SetDestinationCompatibilityAsync(runTask, destination, cancellationToken);
    }

    private static async Task<bool> SetDestinationCompatibilityAsync(
        Task<GradingRun?> runTask,
        string destination,
        CancellationToken cancellationToken)
    {
        var run = await runTask;
        if (run is null) return false;
        try
        {
            run.SetDestination(destination);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    Task<GradingRun?> GetGradingRunAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<GradingRun?>(null);

    Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByGradingRunAsync(
        Guid gradingRunId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AssistedGradingBatch>>([]);

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

    Task<AssistedGradingItem?> FindItemBySubmissionAsync(long submissionId, CancellationToken cancellationToken) =>
        Task.FromResult<AssistedGradingItem?>(null);

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

    /// <summary>
    /// Reads one globally ordered page from a durable grading run. The
    /// PostgreSQL implementation performs the join and filtering in SQL so a
    /// 10k-item run does not get materialized for every page request.
    /// </summary>
    async Task<IReadOnlyList<AssistedGradingItem>> ListItemsByGradingRunAsync(
        Guid gradingRunId,
        int page,
        int pageSize,
        GradingItemStatus? status,
        CancellationToken cancellationToken)
    {
        var batches = await ListBatchesByGradingRunAsync(gradingRunId, cancellationToken);
        var items = new List<AssistedGradingItem>();
        foreach (var batch in batches)
        {
            const int compatibilityPageSize = 400;
            var batchPage = 1;
            while (true)
            {
                var batchItems = await ListItemsByBatchAsync(
                    batch.Id,
                    batchPage,
                    compatibilityPageSize,
                    cancellationToken);
                items.AddRange(status is null
                    ? batchItems
                    : batchItems.Where(item => item.Status == status));
                if (batchItems.Count < compatibilityPageSize)
                {
                    break;
                }

                batchPage++;
            }
        }

        return items
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Skip(Math.Max(0, page - 1) * Math.Max(1, pageSize))
            .Take(Math.Max(1, pageSize))
            .ToArray();
    }

    async Task<int> CountItemsByGradingRunAsync(
        Guid gradingRunId,
        GradingItemStatus? status,
        CancellationToken cancellationToken)
    {
        var batches = await ListBatchesByGradingRunAsync(gradingRunId, cancellationToken);
        var count = 0;
        foreach (var batch in batches)
        {
            const int compatibilityPageSize = 400;
            var batchPage = 1;
            while (true)
            {
                var batchItems = await ListItemsByBatchAsync(
                    batch.Id,
                    batchPage,
                    compatibilityPageSize,
                    cancellationToken);
                count += status is null
                    ? batchItems.Count
                    : batchItems.Count(item => item.Status == status);
                if (batchItems.Count < compatibilityPageSize)
                {
                    break;
                }

                batchPage++;
            }
        }

        return count;
    }

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Claims all requested Moodle grade targets atomically. The default is a
    /// compatibility no-op for legacy test stores; the PostgreSQL repository
    /// enforces a partial unique index across active publication states.
    /// </summary>
    Task<IReadOnlyList<GradingPublicationClaimResult>> TryClaimPublicationTargetsAsync(
        Guid publicationId,
        string connectionKey,
        IReadOnlyCollection<GradingPublicationClaimRequest> requests,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GradingPublicationClaimResult>>(
            requests.Select(request => new GradingPublicationClaimResult(request.GradingItemId, true)).ToArray());

    Task ReleasePublicationClaimsAsync(
        Guid publicationId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Promotes preview claims to an executing publication. Preview expiry no
    /// longer releases the target once the human authorization has been used;
    /// the claim is held until completion or reconciliation.
    /// </summary>
    Task ActivatePublicationClaimsAsync(
        Guid publicationId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Binds a preview claim to the pending action created for it. The
    /// operation is idempotent and prevents expiry cleanup from releasing an
    /// authorized publication during a crash/restart window.
    /// </summary>
    Task BindPublicationClaimsAsync(
        Guid publicationId,
        Guid pendingActionId,
        CancellationToken cancellationToken) => Task.CompletedTask;

}
