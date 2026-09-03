using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Infrastructure;

public sealed class GradingReviewRepository(ConnectorDbContext dbContext) : IGradingReviewRepository, IGradingBatchJobStore, IGradingContextSnapshotStore, IGradingProposalStore, IGradingRetentionStore
{
    private static readonly TimeSpan FairnessAgingThreshold = TimeSpan.FromMinutes(30);

    public async Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken)
    {
        await dbContext.GradingBatches.AddAsync(batch, cancellationToken);
    }

    public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.GradingBatches.SingleOrDefaultAsync(batch => batch.Id == id, cancellationToken);
    }

    public Task<AssistedGradingBatch?> GetBatchByIdempotencyKeyAsync(
        string createdBySubject,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return dbContext.GradingBatches.SingleOrDefaultAsync(batch =>
            batch.CreatedBySubject == createdBySubject &&
            batch.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }

    public async Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken)
    {
        await dbContext.GradingItems.AddAsync(item, cancellationToken);
    }

    public async Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken)
    {
        await dbContext.GradingArtifacts.AddAsync(artifact, cancellationToken);
    }

    public Task UpdateArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var tracked = dbContext.GradingArtifacts.Local.SingleOrDefault(item => item.Id == artifact.Id);
        if (tracked is not null)
        {
            dbContext.Entry(tracked).CurrentValues.SetValues(artifact);
        }
        else
        {
            dbContext.GradingArtifacts.Update(artifact);
        }

        return Task.CompletedTask;
    }

    public async Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken)
    {
        await dbContext.GradingEvidence.AddAsync(evidence, cancellationToken);
    }

    public async Task PublishAsync(
        GradingContextSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var existsInUnitOfWork = dbContext.GradingContextSnapshots.Local.Any(document =>
            document.GradingItemId == snapshot.ItemId &&
            document.Version == snapshot.Version &&
            document.ContextHash == snapshot.ContextHash);
        if (existsInUnitOfWork)
        {
            return;
        }

        var exists = await dbContext.GradingContextSnapshots.AnyAsync(document =>
            document.GradingItemId == snapshot.ItemId &&
            document.Version == snapshot.Version &&
            document.ContextHash == snapshot.ContextHash,
            cancellationToken);
        if (exists)
        {
            return;
        }

        await dbContext.GradingContextSnapshots.AddAsync(
            GradingContextSnapshotDocument.FromSnapshot(snapshot),
            cancellationToken);
    }

    public async Task<int> GetNextVersionAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken)
    {
        if (gradingItemId == Guid.Empty)
        {
            throw new ArgumentException("O item e obrigatorio.", nameof(gradingItemId));
        }

        var persistedCurrent = await dbContext.AiGradingProposals
            .Where(proposal => proposal.GradingItemId == gradingItemId)
            .Select(proposal => (int?)proposal.Version)
            .MaxAsync(cancellationToken);
        var localCurrent = dbContext.AiGradingProposals.Local
            .Where(proposal => proposal.GradingItemId == gradingItemId)
            .Select(proposal => (int?)proposal.Version)
            .Max() ?? 0;
        return Math.Max(persistedCurrent ?? 0, localCurrent) + 1;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetNextVersionsAsync(
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken)
    {
        if (gradingItemIds.Count == 0) return new Dictionary<Guid, int>();
        var persisted = await dbContext.AiGradingProposals
            .Where(proposal => gradingItemIds.Contains(proposal.GradingItemId))
            .GroupBy(proposal => proposal.GradingItemId)
            .Select(group => new { group.Key, Version = group.Max(proposal => proposal.Version) })
            .ToDictionaryAsync(row => row.Key, row => row.Version, cancellationToken);
        foreach (var itemId in gradingItemIds)
        {
            var local = dbContext.AiGradingProposals.Local
                .Where(proposal => proposal.GradingItemId == itemId)
                .Select(proposal => (int?)proposal.Version)
                .Max() ?? 0;
            persisted[itemId] = Math.Max(persisted.GetValueOrDefault(itemId), local) + 1;
        }
        return persisted;
    }

    public async Task PublishAsync(
        AiGradingProposal proposal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var existsInUnitOfWork = dbContext.AiGradingProposals.Local.Any(document =>
            document.GradingItemId == proposal.ItemId &&
            document.Version == proposal.Version &&
            document.ProposalHash == proposal.ProposalHash);
        if (existsInUnitOfWork)
        {
            return;
        }

        var exists = await dbContext.AiGradingProposals.AnyAsync(document =>
            document.GradingItemId == proposal.ItemId &&
            document.Version == proposal.Version &&
            document.ProposalHash == proposal.ProposalHash,
            cancellationToken);
        if (exists)
        {
            return;
        }

        await dbContext.AiGradingProposals.AddAsync(
            AiGradingProposalDocument.FromProposal(proposal),
            cancellationToken);
    }

    public async Task PublishManyAsync(
        IReadOnlyCollection<AiGradingProposal> proposals,
        CancellationToken cancellationToken)
    {
        if (proposals.Count == 0) return;
        var itemIds = proposals.Select(proposal => proposal.ItemId).Distinct().ToArray();
        var hashes = (await dbContext.AiGradingProposals.AsNoTracking()
                .Where(document => itemIds.Contains(document.GradingItemId))
                .Select(document => new { document.GradingItemId, document.Version, document.ProposalHash })
                .ToArrayAsync(cancellationToken))
            .Select(document => (document.GradingItemId, document.Version, document.ProposalHash))
            .ToHashSet();
        foreach (var proposal in proposals)
        {
            var key = (proposal.ItemId, proposal.Version, proposal.ProposalHash);
            var localExists = dbContext.AiGradingProposals.Local.Any(document =>
                document.GradingItemId == proposal.ItemId &&
                document.Version == proposal.Version &&
                document.ProposalHash == proposal.ProposalHash);
            if (!hashes.Contains(key) && !localExists)
            {
                await dbContext.AiGradingProposals.AddAsync(
                    AiGradingProposalDocument.FromProposal(proposal), cancellationToken);
                hashes.Add(key);
            }
        }
    }

    public async Task<int> RedactExpiredArtifactTextAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        if (cutoff <= DateTimeOffset.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(cutoff));
        }

        if (IsInMemory)
        {
            var expired = await dbContext.GradingArtifacts
                .Where(artifact => artifact.CreatedAt < cutoff &&
                                   artifact.ArtifactType == "submission_file" &&
                                   artifact.ExtractedTextRef != null)
                .ToArrayAsync(cancellationToken);
            if (expired.Length == 0)
            {
                return 0;
            }

            dbContext.GradingArtifacts.RemoveRange(expired);
            await dbContext.SaveChangesAsync(cancellationToken);
            foreach (var artifact in expired)
            {
                await dbContext.GradingArtifacts.AddAsync(
                    artifact with
                    {
                        ExtractedTextRef = null,
                        SummaryRef = "retention_redacted"
                    },
                    cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return expired.Length;
        }

        return await dbContext.GradingArtifacts
            .Where(artifact => artifact.CreatedAt < cutoff &&
                               artifact.ArtifactType == "submission_file" &&
                               artifact.ExtractedTextRef != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(artifact => artifact.ExtractedTextRef, (string?)null)
                .SetProperty(artifact => artifact.SummaryRef, "retention_redacted"),
                cancellationToken);
    }

    public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.GradingItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public Task<AssistedGradingItem?> FindItemBySubmissionAsync(long submissionId, CancellationToken cancellationToken) =>
        dbContext.GradingItems.OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(item => item.SubmissionId == submissionId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, AssistedGradingItem>> GetItemsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return new Dictionary<Guid, AssistedGradingItem>();
        return (await dbContext.GradingItems
                .Where(item => ids.Contains(item.Id))
                .ToArrayAsync(cancellationToken))
            .ToDictionary(item => item.Id);
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

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<GradingArtifact>>> ListArtifactsByItemsAsync(
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken)
    {
        if (gradingItemIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<GradingArtifact>>();
        }

        return (await dbContext.GradingArtifacts.AsNoTracking()
                .Where(artifact => gradingItemIds.Contains(artifact.GradingItemId))
                .OrderBy(artifact => artifact.CreatedAt)
                .ToArrayAsync(cancellationToken))
            .GroupBy(artifact => artifact.GradingItemId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<GradingArtifact>)group.ToArray());
    }

    public async Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken)
    {
        return await dbContext.GradingEvidence
            .Where(evidence => evidence.GradingItemId == gradingItemId)
            .OrderBy(evidence => evidence.CreatedAt)
            .ThenBy(evidence => evidence.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<GradingEvidence>>> ListEvidenceByItemsAsync(
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken)
    {
        if (gradingItemIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<GradingEvidence>>();
        }

        return (await dbContext.GradingEvidence.AsNoTracking()
                .Where(evidence => gradingItemIds.Contains(evidence.GradingItemId))
                .OrderBy(evidence => evidence.CreatedAt)
                .ThenBy(evidence => evidence.Id)
                .ToArrayAsync(cancellationToken))
            .GroupBy(evidence => evidence.GradingItemId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<GradingEvidence>)group.ToArray());
    }

    public async Task<IReadOnlyDictionary<Guid, GradingContextSnapshotDocument>> ListLatestContextSnapshotsByItemsAsync(
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken)
    {
        if (gradingItemIds.Count == 0)
        {
            return new Dictionary<Guid, GradingContextSnapshotDocument>();
        }

        var snapshots = await dbContext.GradingContextSnapshots.AsNoTracking()
            .Where(snapshot => gradingItemIds.Contains(snapshot.GradingItemId))
            .OrderByDescending(snapshot => snapshot.Version)
            .ToArrayAsync(cancellationToken);
        return snapshots
            .GroupBy(snapshot => snapshot.GradingItemId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public async Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
        GradingBatchStatus status,
        CancellationToken cancellationToken)
    {
        return await dbContext.GradingBatches
            .Where(batch => batch.Status == status)
            .OrderBy(batch => batch.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(
        string createdBySubject,
        CancellationToken cancellationToken)
    {
        return await dbContext.GradingBatches
            .Where(batch => batch.CreatedBySubject == createdBySubject)
            .OrderByDescending(batch => batch.CreatedAt)
            .Take(50)
            .ToArrayAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GradingBatchLeaseClaim>> ClaimDueBatchesAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxBatches,
        CancellationToken cancellationToken)
    {
        ValidateJobArguments(workerId, leaseDuration);
        var safeMaxBatches = Math.Clamp(maxBatches, 1, 100);
        var normalizedWorkerId = workerId.Trim();
        var agingCutoff = now.Subtract(FairnessAgingThreshold);

        var candidates = await dbContext.GradingBatches
            .AsNoTracking()
            .Where(batch =>
                (batch.Status == GradingBatchStatus.Pending ||
                 (batch.Status == GradingBatchStatus.Processing &&
                  dbContext.GradingItems.Any(item => item.BatchId == batch.Id && item.Status == GradingItemStatus.Pending))) &&
                (batch.NextAttemptAt == null || batch.NextAttemptAt <= now) &&
                (batch.LeaseUntil == null || batch.LeaseUntil <= now || batch.LeaseOwner == normalizedWorkerId))
            // Aged jobs are promoted before priority so a low-priority queue
            // cannot starve indefinitely under sustained high-priority load.
            .OrderBy(batch => batch.CreatedAt <= agingCutoff ? 0 : 1)
            .ThenBy(batch => batch.Priority == "high" ? 0 : batch.Priority == "normal" ? 1 : 2)
            .ThenBy(batch => batch.CreatedAt)
            .ThenBy(batch => batch.Id)
            .Select(batch => batch.Id)
            .Take(safeMaxBatches)
            .ToArrayAsync(cancellationToken);

        var claims = new List<GradingBatchLeaseClaim>(candidates.Length);
        foreach (var batchId in candidates)
        {
            var claim = await TryClaimBatchAsync(
                batchId,
                normalizedWorkerId,
                now,
                leaseDuration,
                cancellationToken);
            if (claim is not null)
            {
                claims.Add(claim);
            }
        }

        return claims;
    }

    public async Task<GradingBatchLeaseClaim?> TryClaimBatchAsync(
        Guid batchId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateJobArguments(workerId, leaseDuration);
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        var normalizedWorkerId = workerId.Trim();
        var leaseUntil = now.Add(leaseDuration);

        if (IsInMemory)
        {
            var inMemoryBatch = await dbContext.GradingBatches
                .SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);
            if (inMemoryBatch is null ||
                inMemoryBatch.Status == GradingBatchStatus.Processing &&
                (!await dbContext.GradingItems.AnyAsync(item => item.BatchId == batchId && item.Status == GradingItemStatus.Pending, cancellationToken) ||
                 inMemoryBatch.NextAttemptAt is { } inMemoryNextAttempt && inMemoryNextAttempt > now) ||
                !inMemoryBatch.TryAcquireLease(normalizedWorkerId, now, leaseDuration))
            {
                return null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new GradingBatchLeaseClaim(
                inMemoryBatch.Id,
                normalizedWorkerId,
                inMemoryBatch.LeaseUntil!.Value,
                inMemoryBatch.AttemptCount);
        }

        var current = await dbContext.GradingBatches
            .AsNoTracking()
            .SingleOrDefaultAsync(batch => batch.Id == batchId, cancellationToken);
        if (current is null ||
            current.Status is GradingBatchStatus.Completed or GradingBatchStatus.Cancelled ||
            (current.Status == GradingBatchStatus.Processing &&
             !await dbContext.GradingItems.AnyAsync(item => item.BatchId == batchId && item.Status == GradingItemStatus.Pending, cancellationToken)) ||
            current.NextAttemptAt is { } nextAttemptAt && nextAttemptAt > now)
        {
            return null;
        }

        var ownsActiveLease = string.Equals(current.LeaseOwner, normalizedWorkerId, StringComparison.Ordinal) &&
            current.LeaseUntil is { } activeLeaseUntil &&
            activeLeaseUntil > now;
        var attemptCount = ownsActiveLease ? current.AttemptCount : current.AttemptCount + 1;
        var updated = await dbContext.GradingBatches
            .Where(batch => batch.Id == batchId &&
                            (batch.Status == GradingBatchStatus.Pending || batch.Status == GradingBatchStatus.Processing) &&
                            (batch.NextAttemptAt == null || batch.NextAttemptAt <= now) &&
                            (batch.LeaseUntil == null || batch.LeaseUntil <= now || batch.LeaseOwner == normalizedWorkerId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(batch => batch.Status, GradingBatchStatus.Processing)
                .SetProperty(batch => batch.LeaseOwner, normalizedWorkerId)
                .SetProperty(batch => batch.LeaseUntil, leaseUntil)
                .SetProperty(batch => batch.AttemptCount, attemptCount)
                .SetProperty(batch => batch.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(batch => batch.UpdatedAt, now), cancellationToken);

        if (updated != 1)
        {
            return null;
        }

        await RefreshTrackedBatchAsync(batchId, cancellationToken);
        return new GradingBatchLeaseClaim(batchId, normalizedWorkerId, leaseUntil, attemptCount);
    }

    public async Task<bool> RenewBatchLeaseAsync(
        Guid batchId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateJobArguments(workerId, leaseDuration);
        var normalizedWorkerId = workerId.Trim();

        if (IsInMemory)
        {
            var batch = await dbContext.GradingBatches.SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);
            if (batch is null || !batch.RenewLease(normalizedWorkerId, now, leaseDuration))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updated = await dbContext.GradingBatches
            .Where(batch => batch.Id == batchId &&
                            batch.Status == GradingBatchStatus.Processing &&
                            batch.LeaseOwner == normalizedWorkerId &&
                            batch.LeaseUntil != null && batch.LeaseUntil > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(batch => batch.LeaseUntil, now.Add(leaseDuration))
                .SetProperty(batch => batch.UpdatedAt, now), cancellationToken) == 1;
        if (updated)
        {
            await RefreshTrackedBatchAsync(batchId, cancellationToken);
        }

        return updated;
    }

    public async Task<bool> ReleaseBatchLeaseAsync(
        Guid batchId,
        string workerId,
        DateTimeOffset now,
        string? errorCode,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty || string.IsNullOrWhiteSpace(workerId))
        {
            return false;
        }

        var normalizedWorkerId = workerId.Trim();
        var normalizedErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : errorCode.Trim()[..Math.Min(120, errorCode.Trim().Length)];

        if (IsInMemory)
        {
            var batch = await dbContext.GradingBatches.SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);
            if (batch is null || !batch.ReleaseLease(normalizedWorkerId, now, normalizedErrorCode, nextAttemptAt))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updated = await dbContext.GradingBatches
            .Where(batch => batch.Id == batchId && batch.LeaseOwner == normalizedWorkerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(batch => batch.LeaseOwner, (string?)null)
                .SetProperty(batch => batch.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(batch => batch.LastErrorCode, normalizedErrorCode)
                .SetProperty(batch => batch.NextAttemptAt, nextAttemptAt)
                .SetProperty(batch => batch.UpdatedAt, now), cancellationToken) == 1;
        if (updated)
        {
            await RefreshTrackedBatchAsync(batchId, cancellationToken);
        }

        return updated;
    }

    public async Task<bool> UpdateBatchCheckpointAsync(
        Guid batchId,
        string workerId,
        Guid itemId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty || itemId == Guid.Empty || string.IsNullOrWhiteSpace(workerId))
        {
            return false;
        }

        var normalizedWorkerId = workerId.Trim();
        var itemBelongsToBatch = await dbContext.GradingItems
            .AnyAsync(item => item.Id == itemId && item.BatchId == batchId, cancellationToken);
        if (!itemBelongsToBatch)
        {
            return false;
        }

        if (IsInMemory)
        {
            var batch = await dbContext.GradingBatches.SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);
            if (batch is null || !batch.UpdateCheckpoint(normalizedWorkerId, itemId, now))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updated = await dbContext.GradingBatches
            .Where(batch => batch.Id == batchId &&
                            batch.Status == GradingBatchStatus.Processing &&
                            batch.LeaseOwner == normalizedWorkerId &&
                            batch.LeaseUntil != null && batch.LeaseUntil > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(batch => batch.CheckpointItemId, itemId)
                .SetProperty(batch => batch.UpdatedAt, now), cancellationToken) == 1;
        if (updated)
        {
            await RefreshTrackedBatchAsync(batchId, cancellationToken);
        }

        return updated;
    }

    public async Task<int> RecoverExpiredBatchLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (IsInMemory)
        {
            var batches = await dbContext.GradingBatches
                .Where(batch => batch.Status == GradingBatchStatus.Processing &&
                                batch.LeaseUntil != null && batch.LeaseUntil <= now &&
                                dbContext.GradingItems.Any(item => item.BatchId == batch.Id && item.Status == GradingItemStatus.Pending))
                .ToArrayAsync(cancellationToken);
            var inMemoryRecovered = batches.Count(batch => batch.RecoverExpiredLease(now));
            if (inMemoryRecovered > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return inMemoryRecovered;
        }

        var relationalRecovered = await dbContext.GradingBatches
            .Where(batch => batch.Status == GradingBatchStatus.Processing &&
                            batch.LeaseUntil != null && batch.LeaseUntil <= now &&
                            dbContext.GradingItems.Any(item => item.BatchId == batch.Id && item.Status == GradingItemStatus.Pending))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(batch => batch.Status, GradingBatchStatus.Pending)
                .SetProperty(batch => batch.LeaseOwner, (string?)null)
                .SetProperty(batch => batch.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(batch => batch.NextAttemptAt, now)
                .SetProperty(batch => batch.UpdatedAt, now), cancellationToken);

        // A crashed worker may have finished all pending items before losing its
        // lease. In that case clear only the lease and preserve the Processing
        // state so the poller does not spin on an already drained batch.
        var releasedWithoutPending = await dbContext.GradingBatches
            .Where(batch => batch.Status == GradingBatchStatus.Processing &&
                            batch.LeaseUntil != null && batch.LeaseUntil <= now &&
                            !dbContext.GradingItems.Any(item => item.BatchId == batch.Id && item.Status == GradingItemStatus.Pending))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(batch => batch.LeaseOwner, (string?)null)
                .SetProperty(batch => batch.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(batch => batch.UpdatedAt, now), cancellationToken);

        return relationalRecovered + releasedWithoutPending;
    }

    public async Task<GradingItemLeaseClaim?> TryClaimItemAsync(
        Guid batchId,
        Guid itemId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateItemJobArguments(batchId, itemId, workerId, leaseDuration);
        var normalizedWorkerId = workerId.Trim();
        var leaseUntil = now.Add(leaseDuration);

        if (IsInMemory)
        {
            var item = await dbContext.GradingItems
                .SingleOrDefaultAsync(candidate => candidate.Id == itemId && candidate.BatchId == batchId, cancellationToken);
            if (item is null || !item.TryAcquireLease(normalizedWorkerId, now, leaseDuration))
            {
                return null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new GradingItemLeaseClaim(
                batchId,
                itemId,
                normalizedWorkerId,
                item.LeaseUntil!.Value,
                item.AttemptCount);
        }

        // The attempt increment is performed in the UPDATE itself so two
        // replicas cannot both read and overwrite the same counter.
        var updated = await dbContext.GradingItems
            .Where(item => item.Id == itemId &&
                           item.BatchId == batchId &&
                           item.Status == GradingItemStatus.Pending &&
                           (item.NextAttemptAt == null || item.NextAttemptAt <= now) &&
                           (item.LeaseUntil == null || item.LeaseUntil <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseOwner, normalizedWorkerId)
                .SetProperty(item => item.LeaseUntil, leaseUntil)
                .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                .SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (updated != 1)
        {
            return null;
        }

        await RefreshTrackedItemAsync(batchId, itemId, cancellationToken);

        var claimed = await dbContext.GradingItems
            .AsNoTracking()
            .Where(item => item.Id == itemId && item.BatchId == batchId)
            .Select(item => new { item.AttemptCount })
            .SingleAsync(cancellationToken);
        return new GradingItemLeaseClaim(
            batchId,
            itemId,
            normalizedWorkerId,
            leaseUntil,
            claimed.AttemptCount);
    }

    public async Task<bool> RenewItemLeaseAsync(
        Guid batchId,
        Guid itemId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateItemJobArguments(batchId, itemId, workerId, leaseDuration);
        var normalizedWorkerId = workerId.Trim();

        if (IsInMemory)
        {
            var item = await dbContext.GradingItems
                .SingleOrDefaultAsync(candidate => candidate.Id == itemId && candidate.BatchId == batchId, cancellationToken);
            if (item is null || !item.RenewLease(normalizedWorkerId, now, leaseDuration))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updated = await dbContext.GradingItems
            .Where(item => item.Id == itemId &&
                           item.BatchId == batchId &&
                           item.Status == GradingItemStatus.Pending &&
                           item.LeaseOwner == normalizedWorkerId &&
                           item.LeaseUntil != null && item.LeaseUntil > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseUntil, now.Add(leaseDuration))
                .SetProperty(item => item.UpdatedAt, now), cancellationToken) == 1;
        if (updated)
        {
            await RefreshTrackedItemAsync(batchId, itemId, cancellationToken);
        }

        return updated;
    }

    public async Task<bool> ReleaseItemLeaseAsync(
        Guid batchId,
        Guid itemId,
        string workerId,
        DateTimeOffset now,
        string? errorCode,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty || itemId == Guid.Empty || string.IsNullOrWhiteSpace(workerId))
        {
            return false;
        }

        var normalizedWorkerId = workerId.Trim();
        var normalizedErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : errorCode.Trim()[..Math.Min(120, errorCode.Trim().Length)];

        if (IsInMemory)
        {
            var item = await dbContext.GradingItems
                .SingleOrDefaultAsync(candidate => candidate.Id == itemId && candidate.BatchId == batchId, cancellationToken);
            if (item is null || !item.ReleaseLease(normalizedWorkerId, now, normalizedErrorCode, nextAttemptAt))
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updated = await dbContext.GradingItems
            .Where(item => item.Id == itemId &&
                           item.BatchId == batchId &&
                           item.LeaseOwner == normalizedWorkerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.LastErrorCode, normalizedErrorCode)
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken) == 1;
        if (updated)
        {
            await RefreshTrackedItemAsync(batchId, itemId, cancellationToken);
        }

        return updated;
    }

    public async Task<int> RecoverExpiredItemLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (IsInMemory)
        {
            var items = await dbContext.GradingItems
                .Where(item => item.Status == GradingItemStatus.Pending &&
                               item.LeaseUntil != null && item.LeaseUntil <= now)
                .ToArrayAsync(cancellationToken);
            var recovered = items.Count(item => item.RecoverExpiredLease(now));
            if (recovered > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return recovered;
        }

        return await dbContext.GradingItems
            .Where(item => item.Status == GradingItemStatus.Pending &&
                           item.LeaseUntil != null && item.LeaseUntil <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.NextAttemptAt, now)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
    }

    private bool IsInMemory => string.Equals(
        dbContext.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.InMemory",
        StringComparison.Ordinal);

    private static void ValidateJobArguments(string workerId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException("O worker do lote e obrigatorio.", nameof(workerId));
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "A duracao do lease deve ser positiva.");
        }
    }

    private static void ValidateItemJobArguments(
        Guid batchId,
        Guid itemId,
        string workerId,
        TimeSpan leaseDuration)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("O item e obrigatorio.", nameof(itemId));
        }

        ValidateJobArguments(workerId, leaseDuration);
    }

    private async Task RefreshTrackedBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var tracked = dbContext.GradingBatches.Local.SingleOrDefault(batch => batch.Id == batchId);
        if (tracked is not null)
        {
            await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
        }
    }

    private async Task RefreshTrackedItemAsync(
        Guid batchId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var tracked = dbContext.GradingItems.Local.SingleOrDefault(item =>
            item.Id == itemId && item.BatchId == batchId);
        if (tracked is not null)
        {
            await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
        }
    }
}
