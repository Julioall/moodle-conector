using Microsoft.EntityFrameworkCore;
using Npgsql;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Infrastructure;

public sealed class GradingReviewRepository(ConnectorDbContext dbContext) : IGradingReviewRepository, IGradingBatchJobStore, IGradingContextSnapshotStore, IGradingProposalStore, IGradingRetentionStore
{
    private static readonly TimeSpan FairnessAgingThreshold = TimeSpan.FromMinutes(30);

    public async Task AddGradingRunAsync(GradingRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await dbContext.GradingRuns.AddAsync(run, cancellationToken);
    }

    public async Task<bool> TrySetGradingRunDestinationAsync(
        Guid gradingRunId,
        string destination,
        CancellationToken cancellationToken)
    {
        var normalizedDestination = string.IsNullOrWhiteSpace(destination)
            ? "undecided"
            : destination.Trim().ToLowerInvariant();
        if (normalizedDestination is not ("undecided" or "csv" or "publish"))
        {
            throw new ArgumentException("O destino deve ser undecided, csv ou publish.", nameof(destination));
        }

        if (IsInMemory)
        {
            var run = await dbContext.GradingRuns
                .SingleOrDefaultAsync(candidate => candidate.Id == gradingRunId, cancellationToken);
            if (run is null)
            {
                return false;
            }

            try
            {
                run.SetDestination(normalizedDestination);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        // The conditional predicate makes CSV vs publish a database-level
        // mutex even when two requests load the same run concurrently.
        var updated = await dbContext.GradingRuns
            .Where(run => run.Id == gradingRunId &&
                          (run.Destination == "undecided" || run.Destination == normalizedDestination))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Destination, normalizedDestination)
                .SetProperty(run => run.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (updated == 1)
        {
            return true;
        }

        var observed = await dbContext.GradingRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(run => run.Id == gradingRunId, cancellationToken);
        return observed is not null && string.Equals(observed.Destination, normalizedDestination, StringComparison.Ordinal);
    }

    public Task<GradingRun?> GetGradingRunAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.GradingRuns.SingleOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByGradingRunAsync(
        Guid gradingRunId,
        CancellationToken cancellationToken)
    {
        if (gradingRunId == Guid.Empty)
        {
            return [];
        }

        return await QueryBatchesForRun(gradingRunId)
            .OrderBy(batch => batch.CreatedAt)
            .ThenBy(batch => batch.Id)
            .ToArrayAsync(cancellationToken);
    }

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

    public async Task<IReadOnlyList<GradingSubmissionMatch>> ListExistingSubmissionMatchesAsync(
        IReadOnlyCollection<GradingSubmissionIdentity> identities,
        string? moodleConnectionId,
        string? connectorClientId,
        string? connectionAlias,
        CancellationToken cancellationToken)
    {
        if (identities.Count == 0)
        {
            return [];
        }

        var courseIds = identities.Select(identity => identity.CourseId).Distinct().ToArray();
        var assignmentIds = identities.Select(identity => identity.AssignmentId).Distinct().ToArray();
        var submissionIds = identities.Select(identity => identity.SubmissionId).Distinct().ToArray();

        var query = BuildSubmissionMatchQuery(
            submissionIds,
            courseIds,
            assignmentIds,
            moodleConnectionId,
            connectorClientId,
            connectionAlias,
            createdBySubject: null);
        var matches = await MaterializeSubmissionMatchesAsync(query, cancellationToken);
        var expected = identities.ToHashSet();
        return matches
            .Where(match => expected.Contains(match.Identity))
            .DistinctBy(match => match.GradingItemId)
            .ToArray();
    }

    public async Task<IReadOnlyList<GradingSubmissionMatch>> FindSubmissionMatchesAsync(
        long submissionId,
        long? courseId,
        long? assignmentId,
        string? moodleConnectionId,
        string? connectorClientId,
        string? connectionAlias,
        string? createdBySubject,
        CancellationToken cancellationToken)
    {
        if (submissionId <= 0)
        {
            return [];
        }

        var query = BuildSubmissionMatchQuery(
            [submissionId],
            courseId is long requestedCourseId ? [requestedCourseId] : null,
            assignmentId is long requestedAssignmentId ? [requestedAssignmentId] : null,
            moodleConnectionId,
            connectorClientId,
            connectionAlias,
            createdBySubject);
        return await MaterializeSubmissionMatchesAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<GradingSubmissionIdentity>> ListExistingSubmissionIdentitiesAsync(
        IReadOnlyCollection<GradingSubmissionIdentity> identities,
        string? moodleConnectionId,
        string? connectorClientId,
        string? connectionAlias,
        CancellationToken cancellationToken)
    {
        var matches = await ListExistingSubmissionMatchesAsync(
            identities,
            moodleConnectionId,
            connectorClientId,
            connectionAlias,
            cancellationToken);
        return matches.Select(match => match.Identity).Distinct().ToArray();
    }

    private IQueryable<GradingSubmissionMatchProjection> BuildSubmissionMatchQuery(
        IReadOnlyCollection<long> submissionIds,
        IReadOnlyCollection<long>? courseIds,
        IReadOnlyCollection<long>? assignmentIds,
        string? moodleConnectionId,
        string? connectorClientId,
        string? connectionAlias,
        string? createdBySubject)
    {
        var query = from item in dbContext.GradingItems.AsNoTracking()
                    join batch in dbContext.GradingBatches.AsNoTracking()
                        on item.BatchId equals batch.Id
                    where item.SubmissionId.HasValue &&
                          submissionIds.Contains(item.SubmissionId.Value) &&
                          (batch.Status != GradingBatchStatus.Cancelled ||
                           item.Status == GradingItemStatus.Committed ||
                           item.CommitStatus == GradingCommitStatus.Succeeded ||
                           item.CommitStatus == GradingCommitStatus.ExecutionUnknown)
                    select new GradingSubmissionMatchProjection
                    {
                        CourseId = item.CourseId,
                        AssignmentId = item.AssignmentId,
                        SubmissionId = item.SubmissionId!.Value,
                        AttemptNumber = item.AttemptNumber,
                        GradingItemId = item.Id,
                        BatchJobId = batch.Id,
                        GradingRunId = batch.GradingRunId,
                        CreatedBySubject = batch.CreatedBySubject,
                        ItemStatus = item.Status,
                        CommitStatus = item.CommitStatus,
                        BatchStatus = batch.Status,
                        MoodleConnectionId = batch.MoodleConnectionId,
                        ConnectorClientId = batch.ConnectorClientId,
                        ConnectionAlias = batch.ConnectionAlias
                    };

        if (courseIds is not null && courseIds.Count > 0)
        {
            query = query.Where(row => courseIds.Contains(row.CourseId));
        }

        if (assignmentIds is not null && assignmentIds.Count > 0)
        {
            query = query.Where(row => assignmentIds.Contains(row.AssignmentId));
        }

        if (!string.IsNullOrWhiteSpace(createdBySubject))
        {
            var normalizedSubject = createdBySubject.Trim();
            query = query.Where(row => row.CreatedBySubject == normalizedSubject);
        }

        // Prefer the stable connection id. During rollout, old batches may
        // only have client/alias (or no connection metadata), so retain those
        // legacy rows as a safe duplicate barrier as well.
        if (!string.IsNullOrWhiteSpace(moodleConnectionId))
        {
            var normalizedConnectionId = moodleConnectionId.Trim();
            var normalizedClientId = string.IsNullOrWhiteSpace(connectorClientId) ? null : connectorClientId.Trim();
            var normalizedAlias = string.IsNullOrWhiteSpace(connectionAlias) ? null : connectionAlias.Trim();
            query = query.Where(row =>
                row.MoodleConnectionId == normalizedConnectionId ||
                (row.MoodleConnectionId == null &&
                 (normalizedClientId == null || row.ConnectorClientId == normalizedClientId) &&
                 (normalizedAlias == null || row.ConnectionAlias == normalizedAlias)));
        }
        else if (!string.IsNullOrWhiteSpace(connectorClientId) || !string.IsNullOrWhiteSpace(connectionAlias))
        {
            var normalizedClientId = string.IsNullOrWhiteSpace(connectorClientId) ? null : connectorClientId.Trim();
            var normalizedAlias = string.IsNullOrWhiteSpace(connectionAlias) ? null : connectionAlias.Trim();
            query = query.Where(row =>
                (normalizedClientId == null || row.ConnectorClientId == normalizedClientId) &&
                (normalizedAlias == null || row.ConnectionAlias == normalizedAlias));
        }

        return query;
    }

    private async Task<IReadOnlyList<GradingSubmissionMatch>> MaterializeSubmissionMatchesAsync(
        IQueryable<GradingSubmissionMatchProjection> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToArrayAsync(cancellationToken);
        var runIds = rows
            .Where(row => row.GradingRunId.HasValue)
            .Select(row => row.GradingRunId!.Value)
            .Distinct()
            .ToArray();
        var runs = runIds.Length == 0
            ? new Dictionary<Guid, GradingRunStatus>()
            : await dbContext.GradingRuns.AsNoTracking()
                .Where(run => runIds.Contains(run.Id))
                .ToDictionaryAsync(run => run.Id, run => run.Status, cancellationToken);

        return rows
            .OrderByDescending(row => row.GradingRunId.HasValue && runs.ContainsKey(row.GradingRunId.Value)
                ? runs[row.GradingRunId.Value]
                : GradingRunStatus.Preparing)
            .ThenByDescending(row => row.GradingItemId)
            .Select(row => new GradingSubmissionMatch(
                new GradingSubmissionIdentity(
                    row.CourseId,
                    row.AssignmentId,
                    row.SubmissionId,
                    row.AttemptNumber),
                row.GradingItemId,
                row.BatchJobId,
                row.GradingRunId,
                row.CreatedBySubject,
                row.ItemStatus,
                row.CommitStatus,
                row.BatchStatus,
                row.GradingRunId is Guid runId && runs.TryGetValue(runId, out var runStatus)
                    ? runStatus
                    : null))
            .ToArray();
    }

    private sealed class GradingSubmissionMatchProjection
    {
        public long CourseId { get; init; }
        public long AssignmentId { get; init; }
        public long SubmissionId { get; init; }
        public int? AttemptNumber { get; init; }
        public Guid GradingItemId { get; init; }
        public Guid BatchJobId { get; init; }
        public Guid? GradingRunId { get; init; }
        public string CreatedBySubject { get; init; } = string.Empty;
        public GradingItemStatus ItemStatus { get; init; }
        public GradingCommitStatus CommitStatus { get; init; }
        public GradingBatchStatus BatchStatus { get; init; }
        public string? MoodleConnectionId { get; init; }
        public string? ConnectorClientId { get; init; }
        public string? ConnectionAlias { get; init; }
    }

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
        var safePageSize = Math.Clamp(pageSize, 1, 400);
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

    public async Task<IReadOnlyList<AssistedGradingItem>> ListItemsByGradingRunAsync(
        Guid gradingRunId,
        int page,
        int pageSize,
        GradingItemStatus? status,
        CancellationToken cancellationToken)
    {
        if (gradingRunId == Guid.Empty)
        {
            return [];
        }

        var batchIds = QueryBatchesForRun(gradingRunId).Select(batch => batch.Id);
        var query = dbContext.GradingItems
            .AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId));
        if (status is not null)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        return await query
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Skip(Math.Max(0, page - 1) * Math.Max(1, pageSize))
            .Take(Math.Clamp(pageSize, 1, 400))
            .ToArrayAsync(cancellationToken);
    }

    public Task<int> CountItemsByGradingRunAsync(
        Guid gradingRunId,
        GradingItemStatus? status,
        CancellationToken cancellationToken)
    {
        var batchIds = QueryBatchesForRun(gradingRunId).Select(batch => batch.Id);
        var query = dbContext.GradingItems
            .Where(item => batchIds.Contains(item.BatchId));
        if (status is not null)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        return query.CountAsync(cancellationToken);
    }

    /// <summary>
    /// The persisted foreign key is the primary run lineage. The idempotency
    /// key is an immutable, run-scoped recovery marker written while every
    /// child batch is created. Including an orphaned legacy row prevents a
    /// lineage persistence fault from silently becoming a partial aggregate.
    /// </summary>
    private IQueryable<AssistedGradingBatch> QueryBatchesForRun(Guid gradingRunId)
    {
        var lineagePrefix = $"pending-run:{gradingRunId:N}:";
        return dbContext.GradingBatches.Where(batch =>
            batch.GradingRunId == gradingRunId ||
            (batch.GradingRunId == null &&
             batch.IdempotencyKey != null &&
             batch.IdempotencyKey.StartsWith(lineagePrefix)));
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

    public async Task<GradingLocalPurgeResult> PurgeCancelledGradingDataAsync(
        IReadOnlyCollection<Guid> batchIds,
        Guid? gradingRunId,
        CancellationToken cancellationToken)
    {
        var normalizedBatchIds = batchIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (normalizedBatchIds.Length == 0 && gradingRunId is null)
        {
            return BlockedPurge("Nenhum lote local foi informado para expurgo.");
        }

        var now = DateTimeOffset.UtcNow;
        var batchesQuery = dbContext.GradingBatches
            .Where(batch => normalizedBatchIds.Contains(batch.Id));
        var batches = await batchesQuery.ToArrayAsync(cancellationToken);
        if (batches.Length != normalizedBatchIds.Length)
        {
            return BlockedPurge("Um ou mais sublotes da execucao nao foram encontrados.");
        }

        if (batches.Any(batch => batch.Status != GradingBatchStatus.Cancelled))
        {
            return BlockedPurge("Cancele todos os sublotes antes de remover os dados locais.");
        }

        if (gradingRunId is Guid runId && runId != Guid.Empty)
        {
            var lineageBatchIds = await QueryBatchesForRun(runId)
                .Select(batch => batch.Id)
                .ToArrayAsync(cancellationToken);
            if (lineageBatchIds.Any(id => !normalizedBatchIds.Contains(id)))
            {
                return BlockedPurge("A execucao possui sublotes fora do escopo informado; a limpeza foi interrompida.");
            }
        }

        var itemQuery = dbContext.GradingItems
            .Where(item => normalizedBatchIds.Contains(item.BatchId));
        var items = await itemQuery.ToArrayAsync(cancellationToken);
        if (items.Any(item =>
                item.Status == GradingItemStatus.Committed ||
                item.CommitStatus == GradingCommitStatus.Succeeded))
        {
            return BlockedPurge("A execucao possui itens publicados; os dados historicos foram preservados.");
        }

        if (items.Any(item => item.CommitStatus == GradingCommitStatus.ExecutionUnknown))
        {
            return BlockedPurge("A execucao possui escrita Moodle de resultado desconhecido; reconcilie antes de remover.");
        }

        if (batches.Any(batch => batch.LeaseUntil is { } batchLease && batchLease > now) ||
            items.Any(item => item.LeaseUntil is { } itemLease && itemLease > now))
        {
            return BlockedPurge("A execucao ainda possui worker ativo; aguarde o lease expirar e tente novamente.");
        }

        var itemIds = items.Select(item => item.Id).ToArray();
        var activeClaimCount = itemIds.Length == 0
            ? 0
            : await dbContext.GradingPublicationClaims
                .CountAsync(claim =>
                    itemIds.Contains(claim.GradingItemId) &&
                    (claim.Status == "AwaitingConfirmation" ||
                     claim.Status == "Authorized" ||
                     claim.Status == "Executing" ||
                     claim.Status == "ExecutionUnknown"),
                    cancellationToken);
        if (activeClaimCount > 0)
        {
            return BlockedPurge("A execucao possui uma publicacao pendente ou em execucao; ela nao pode ser removida.");
        }

        if (gradingRunId is Guid existingRunId && existingRunId != Guid.Empty &&
            !await dbContext.GradingRuns.AnyAsync(run => run.Id == existingRunId, cancellationToken))
        {
            return BlockedPurge("A execucao agregada nao foi encontrada.");
        }

        var deletedClaims = 0;
        var deletedProposals = 0;
        var deletedSnapshots = 0;
        var deletedEvidence = 0;
        var deletedArtifacts = 0;
        var deletedItems = 0;
        var deletedBatches = 0;
        var deletedRuns = 0;

        if (IsInMemory)
        {
            var claims = itemIds.Length == 0
                ? []
                : await dbContext.GradingPublicationClaims
                    .Where(claim => itemIds.Contains(claim.GradingItemId))
                    .ToArrayAsync(cancellationToken);
            var proposals = itemIds.Length == 0
                ? []
                : await dbContext.AiGradingProposals
                    .Where(proposal => itemIds.Contains(proposal.GradingItemId))
                    .ToArrayAsync(cancellationToken);
            var snapshots = itemIds.Length == 0
                ? []
                : await dbContext.GradingContextSnapshots
                    .Where(snapshot => itemIds.Contains(snapshot.GradingItemId))
                    .ToArrayAsync(cancellationToken);
            var evidence = itemIds.Length == 0
                ? []
                : await dbContext.GradingEvidence
                    .Where(entry => itemIds.Contains(entry.GradingItemId))
                    .ToArrayAsync(cancellationToken);
            var artifacts = itemIds.Length == 0
                ? []
                : await dbContext.GradingArtifacts
                    .Where(artifact => itemIds.Contains(artifact.GradingItemId))
                    .ToArrayAsync(cancellationToken);
            var inMemoryItems = items;
            var inMemoryBatches = batches;

            dbContext.GradingPublicationClaims.RemoveRange(claims);
            dbContext.AiGradingProposals.RemoveRange(proposals);
            dbContext.GradingContextSnapshots.RemoveRange(snapshots);
            dbContext.GradingEvidence.RemoveRange(evidence);
            dbContext.GradingArtifacts.RemoveRange(artifacts);
            dbContext.GradingItems.RemoveRange(inMemoryItems);
            dbContext.GradingBatches.RemoveRange(inMemoryBatches);
            if (gradingRunId is Guid inMemoryRunId && inMemoryRunId != Guid.Empty)
            {
                var run = await dbContext.GradingRuns
                    .SingleOrDefaultAsync(candidate => candidate.Id == inMemoryRunId, cancellationToken);
                if (run is not null)
                {
                    dbContext.GradingRuns.Remove(run);
                    deletedRuns = 1;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return new GradingLocalPurgeResult(
                Purged: true,
                deletedRuns,
                inMemoryBatches.Length,
                inMemoryItems.Length,
                artifacts.Length,
                evidence.Length,
                snapshots.Length,
                proposals.Length,
                claims.Length);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // O cancelamento já foi persistido pelo handler. Limpar o tracker
            // evita que entidades carregadas antes do expurgo sejam gravadas
            // novamente depois dos ExecuteDelete abaixo.
            dbContext.ChangeTracker.Clear();

            if (itemIds.Length > 0)
            {
                deletedClaims = await dbContext.GradingPublicationClaims
                    .Where(claim => itemIds.Contains(claim.GradingItemId))
                    .ExecuteDeleteAsync(cancellationToken);
                deletedProposals = await dbContext.AiGradingProposals
                    .Where(proposal => itemIds.Contains(proposal.GradingItemId))
                    .ExecuteDeleteAsync(cancellationToken);
                deletedSnapshots = await dbContext.GradingContextSnapshots
                    .Where(snapshot => itemIds.Contains(snapshot.GradingItemId))
                    .ExecuteDeleteAsync(cancellationToken);
                deletedEvidence = await dbContext.GradingEvidence
                    .Where(evidence => itemIds.Contains(evidence.GradingItemId))
                    .ExecuteDeleteAsync(cancellationToken);
                deletedArtifacts = await dbContext.GradingArtifacts
                    .Where(artifact => itemIds.Contains(artifact.GradingItemId))
                    .ExecuteDeleteAsync(cancellationToken);
                deletedItems = await dbContext.GradingItems
                    .Where(item => normalizedBatchIds.Contains(item.BatchId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            deletedBatches = await dbContext.GradingBatches
                .Where(batch => normalizedBatchIds.Contains(batch.Id))
                .ExecuteDeleteAsync(cancellationToken);

            if (gradingRunId is Guid runToDelete && runToDelete != Guid.Empty)
            {
                deletedRuns = await dbContext.GradingRuns
                    .Where(run => run.Id == runToDelete)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }

        return new GradingLocalPurgeResult(
            Purged: true,
            deletedRuns,
            deletedBatches,
            deletedItems,
            deletedArtifacts,
            deletedEvidence,
            deletedSnapshots,
            deletedProposals,
            deletedClaims);

        static GradingLocalPurgeResult BlockedPurge(string reason) => new(
            Purged: false,
            DeletedRuns: 0,
            DeletedBatches: 0,
            DeletedItems: 0,
            DeletedArtifacts: 0,
            DeletedEvidence: 0,
            DeletedContextSnapshots: 0,
            DeletedProposals: 0,
            DeletedPublicationClaims: 0,
            BlockReason: reason);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GradingPublicationClaimResult>> TryClaimPublicationTargetsAsync(
        Guid publicationId,
        string connectionKey,
        IReadOnlyCollection<GradingPublicationClaimRequest> requests,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        if (publicationId == Guid.Empty || string.IsNullOrWhiteSpace(connectionKey) || requests.Count == 0)
        {
            return requests.Select(request => new GradingPublicationClaimResult(
                request.GradingItemId,
                false,
                "invalid_claim_request")).ToArray();
        }

        var normalizedConnectionKey = connectionKey.Trim();
        var normalizedRequests = requests
            .GroupBy(request => (request.AssignmentId, request.MoodleUserId, Attempt: request.AttemptNumber))
            .Select(group => group.First())
            .ToArray();
        var requestedAssignmentIds = normalizedRequests
            .Select(request => request.AssignmentId)
            .Distinct()
            .ToArray();
        var requestedMoodleUserIds = normalizedRequests
            .Select(request => request.MoodleUserId)
            .Distinct()
            .ToArray();
        var activeStatuses = new[] { "AwaitingConfirmation", "Authorized", "Executing", "ExecutionUnknown" };

        if (!IsInMemory)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            try
            {
                // The partial unique index intentionally cannot encode
                // ExpiresAt. Retire stale preview claims before inserting a
                // replacement, otherwise an expired preview would remain a
                // permanent mutex row.
                await dbContext.GradingPublicationClaims
                    .Where(claim =>
                        claim.Status == "AwaitingConfirmation" && claim.ExpiresAt <= now &&
                        (!claim.PendingActionId.HasValue ||
                         !dbContext.PendingMoodleActions.Any(action =>
                             action.Id == claim.PendingActionId.Value &&
                             (action.Status == PendingActionStatus.Confirmed ||
                              action.Status == PendingActionStatus.Authorized ||
                              action.Status == PendingActionStatus.Executing ||
                              action.Status == PendingActionStatus.ExecutionUnknown ||
                              action.Status == PendingActionStatus.PartiallyCompleted))))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(claim => claim.Status, "Released"), cancellationToken);

                var existing = new List<GradingPublicationClaimEntity>();
                // Keep each IN predicate bounded. A 10k-item run can contain
                // thousands of distinct activities/students; one giant
                // Cartesian filter could still pull unrelated claims from a
                // busy connection. Chunking keeps each indexed lookup tied to
                // at most 500 requested target pairs.
                foreach (var requestChunk in normalizedRequests.Chunk(500))
                {
                    var chunkAssignmentIds = requestChunk
                        .Select(request => request.AssignmentId)
                        .Distinct()
                        .ToArray();
                    var chunkMoodleUserIds = requestChunk
                        .Select(request => request.MoodleUserId)
                        .Distinct()
                        .ToArray();
                    existing.AddRange(await dbContext.GradingPublicationClaims
                        .AsNoTracking()
                        .Where(claim => claim.ConnectionKey == normalizedConnectionKey &&
                                        activeStatuses.Contains(claim.Status) &&
                                        // Restrict the lookup to the current
                                        // page chunk's activity/student sets.
                                        chunkAssignmentIds.Contains(claim.AssignmentId) &&
                                        chunkMoodleUserIds.Contains(claim.MoodleUserId) &&
                                        (claim.ExpiresAt > now ||
                                         (claim.Status == "AwaitingConfirmation" &&
                                          claim.PendingActionId.HasValue &&
                                          dbContext.PendingMoodleActions.Any(action =>
                                              action.Id == claim.PendingActionId.Value &&
                                              (action.Status == PendingActionStatus.Confirmed ||
                                               action.Status == PendingActionStatus.Authorized ||
                                               action.Status == PendingActionStatus.Executing ||
                                               action.Status == PendingActionStatus.ExecutionUnknown)))) )
                        .ToArrayAsync(cancellationToken));
                }
                var existingByKey = existing
                    .GroupBy(claim => (claim.AssignmentId, claim.MoodleUserId, claim.AttemptNumber))
                    .ToDictionary(group => group.Key, group => group.First());
                // A large request may overlap another teacher on only a few
                // targets. Keep the non-conflicting targets claimable instead
                // of making an entire 10k-item preview appear busy.
                var claimableRequests = normalizedRequests
                    .Where(request => !existingByKey.TryGetValue(
                                          (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                                          out var existingClaim) ||
                                      existingClaim.PublicationId == publicationId)
                    .ToArray();
                if (claimableRequests.Length == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return normalizedRequests.Select(request =>
                        new GradingPublicationClaimResult(request.GradingItemId, false, "publication_target_busy"))
                        .ToArray();
                }

                // A retry of a PartiallyCompleted publication may still own
                // active rows. Renew those rows atomically instead of trying
                // to insert a duplicate unique key.
                var ownClaimIds = claimableRequests
                    .Select(request => existingByKey.GetValueOrDefault(
                        (request.AssignmentId, request.MoodleUserId, request.AttemptNumber)))
                    .Where(claim => claim is not null && claim.PublicationId == publicationId)
                    .Select(claim => claim!.Id)
                    .ToArray();
                if (ownClaimIds.Length > 0)
                {
                    await dbContext.GradingPublicationClaims
                        .Where(claim => ownClaimIds.Contains(claim.Id))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(claim => claim.ExpiresAt, expiresAt), cancellationToken);
                }

                await dbContext.GradingPublicationClaims.AddRangeAsync(
                    claimableRequests.Where(request => !existingByKey.TryGetValue(
                            (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                            out var existingClaim) || existingClaim.PublicationId != publicationId)
                        .Select(request => new GradingPublicationClaimEntity
                    {
                        PublicationId = publicationId,
                        GradingItemId = request.GradingItemId,
                        ConnectionKey = normalizedConnectionKey,
                        AssignmentId = request.AssignmentId,
                        MoodleUserId = request.MoodleUserId,
                        AttemptNumber = request.AttemptNumber,
                        Status = "AwaitingConfirmation",
                        ExpiresAt = expiresAt,
                        CreatedAt = DateTimeOffset.UtcNow
                    }),
                    cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return normalizedRequests.Select(request =>
                    new GradingPublicationClaimResult(
                        request.GradingItemId,
                        !existingByKey.TryGetValue(
                            (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                            out var existingClaim) || existingClaim.PublicationId == publicationId,
                        existingByKey.TryGetValue(
                            (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                            out existingClaim) && existingClaim.PublicationId != publicationId
                            ? "publication_target_busy"
                            : null))
                    .ToArray();
            }
            catch (DbUpdateException exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                foreach (var entry in dbContext.ChangeTracker.Entries<GradingPublicationClaimEntity>()
                             .Where(entry => entry.State == EntityState.Added))
                {
                    entry.State = EntityState.Detached;
                }

                // Two large previews can race on one target. The unique
                // index correctly rejects the losing transaction, but free
                // targets should still make progress. Split and retry only
                // unique-key conflicts; a different database failure remains
                // a conservative all-busy result.
                if (!IsUniqueViolation(exception) || normalizedRequests.Length <= 1)
                {
                    return normalizedRequests.Select(request => new GradingPublicationClaimResult(
                        request.GradingItemId,
                        false,
                        "publication_target_busy")).ToArray();
                }

                // The recursive retry opens a fresh transaction on the same
                // DbContext; dispose the rolled-back transaction first.
                await transaction.DisposeAsync();
                var midpoint = normalizedRequests.Length / 2;
                var left = await TryClaimPublicationTargetsAsync(
                    publicationId,
                    normalizedConnectionKey,
                    normalizedRequests.Take(midpoint).ToArray(),
                    expiresAt,
                    cancellationToken);
                var right = await TryClaimPublicationTargetsAsync(
                    publicationId,
                    normalizedConnectionKey,
                    normalizedRequests.Skip(midpoint).ToArray(),
                    expiresAt,
                    cancellationToken);
                return left.Concat(right).ToArray();
            }
        }

        var inMemoryClaims = await dbContext.GradingPublicationClaims
            .Where(claim => claim.ConnectionKey == normalizedConnectionKey &&
                            requestedAssignmentIds.Contains(claim.AssignmentId) &&
                            requestedMoodleUserIds.Contains(claim.MoodleUserId))
            .ToArrayAsync(cancellationToken);
        var nowInMemory = DateTimeOffset.UtcNow;
        var protectedActionIds = await dbContext.PendingMoodleActions
            .Where(action => action.Status == PendingActionStatus.Confirmed ||
                             action.Status == PendingActionStatus.Authorized ||
                             action.Status == PendingActionStatus.Executing ||
                             action.Status == PendingActionStatus.ExecutionUnknown)
            .Select(action => action.Id)
            .ToHashSetAsync(cancellationToken);
        foreach (var claim in inMemoryClaims.Where(claim =>
                     claim.Status == "AwaitingConfirmation" && claim.ExpiresAt <= nowInMemory &&
                     (!claim.PendingActionId.HasValue || !protectedActionIds.Contains(claim.PendingActionId.Value))))
        {
            claim.Status = "Released";
        }

        var localExistingByKey = inMemoryClaims
            .Where(claim => activeStatuses.Contains(claim.Status) &&
                            (claim.ExpiresAt > nowInMemory ||
                             (claim.Status == "AwaitingConfirmation" &&
                              claim.PendingActionId.HasValue && protectedActionIds.Contains(claim.PendingActionId.Value))))
            .GroupBy(claim => (claim.AssignmentId, claim.MoodleUserId, claim.AttemptNumber))
            .ToDictionary(group => group.Key, group => group.First());
        var localClaimable = normalizedRequests
            .Where(request => !localExistingByKey.TryGetValue(
                                  (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                                  out var existingClaim) || existingClaim.PublicationId == publicationId)
            .ToArray();
        if (localClaimable.Length == 0)
        {
            return normalizedRequests.Select(request => new GradingPublicationClaimResult(
                request.GradingItemId,
                false,
                "publication_target_busy")).ToArray();
        }

        foreach (var request in localClaimable)
        {
            if (localExistingByKey.TryGetValue(
                    (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                    out var existingClaim) && existingClaim.PublicationId == publicationId)
            {
                existingClaim.ExpiresAt = expiresAt;
            }
        }

        await dbContext.GradingPublicationClaims.AddRangeAsync(
            localClaimable.Where(request => !localExistingByKey.TryGetValue(
                    (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                    out var existingClaim) || existingClaim.PublicationId != publicationId)
                .Select(request => new GradingPublicationClaimEntity
            {
                PublicationId = publicationId,
                GradingItemId = request.GradingItemId,
                ConnectionKey = normalizedConnectionKey,
                AssignmentId = request.AssignmentId,
                MoodleUserId = request.MoodleUserId,
                AttemptNumber = request.AttemptNumber,
                Status = "AwaitingConfirmation",
                ExpiresAt = expiresAt,
                CreatedAt = DateTimeOffset.UtcNow
            }),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return normalizedRequests.Select(request => new GradingPublicationClaimResult(
            request.GradingItemId,
            !localExistingByKey.TryGetValue(
                (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                out var existingClaim) || existingClaim.PublicationId == publicationId,
            localExistingByKey.TryGetValue(
                (request.AssignmentId, request.MoodleUserId, request.AttemptNumber),
                out existingClaim) && existingClaim.PublicationId != publicationId
                ? "publication_target_busy"
                : null)).ToArray();
    }

    public async Task ReleasePublicationClaimsAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        if (publicationId == Guid.Empty)
        {
            return;
        }

        if (IsInMemory)
        {
            var localClaims = await dbContext.GradingPublicationClaims
                .Where(claim => claim.PublicationId == publicationId)
                .ToArrayAsync(cancellationToken);
            foreach (var claim in localClaims)
            {
                claim.Status = "Released";
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.GradingPublicationClaims
            .Where(claim => claim.PublicationId == publicationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(claim => claim.Status, "Released"), cancellationToken);
    }

    public async Task ActivatePublicationClaimsAsync(Guid publicationId, CancellationToken cancellationToken)
    {
        if (publicationId == Guid.Empty)
        {
            return;
        }

        // Authorization owns the target until execution reaches a terminal
        // state; the row remains active even if the preview's 15-minute TTL
        // has elapsed while a worker is being recovered.
        var perpetualClaimExpiry = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);
        if (IsInMemory)
        {
            var localClaims = await dbContext.GradingPublicationClaims
                .Where(claim => claim.PublicationId == publicationId &&
                                (claim.Status == "AwaitingConfirmation" || claim.Status == "Authorized"))
                .ToArrayAsync(cancellationToken);
            foreach (var claim in localClaims)
            {
                claim.Status = "Authorized";
                claim.ExpiresAt = perpetualClaimExpiry;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.GradingPublicationClaims
            .Where(claim => claim.PublicationId == publicationId &&
                            (claim.Status == "AwaitingConfirmation" || claim.Status == "Authorized"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(claim => claim.Status, "Authorized")
                .SetProperty(claim => claim.ExpiresAt, perpetualClaimExpiry),
                cancellationToken);
    }

    public async Task BindPublicationClaimsAsync(
        Guid publicationId,
        Guid pendingActionId,
        CancellationToken cancellationToken)
    {
        if (publicationId == Guid.Empty || pendingActionId == Guid.Empty)
        {
            return;
        }

        if (IsInMemory)
        {
            var localClaims = await dbContext.GradingPublicationClaims
                .Where(claim => claim.PublicationId == publicationId)
                .ToArrayAsync(cancellationToken);
            foreach (var claim in localClaims)
            {
                claim.PendingActionId = pendingActionId;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await dbContext.GradingPublicationClaims
            .Where(claim => claim.PublicationId == publicationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                claim => claim.PendingActionId,
                (Guid?)pendingActionId), cancellationToken);
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

    public async Task<IReadOnlySet<Guid>> TryClaimItemsAsync(
        Guid batchId,
        IReadOnlyCollection<Guid> itemIds,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        ValidateJobArguments(workerId, leaseDuration);
        var normalizedWorkerId = workerId.Trim();
        var normalizedItemIds = itemIds
            .Where(itemId => itemId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (normalizedItemIds.Length == 0)
        {
            return new HashSet<Guid>();
        }

        if (IsInMemory)
        {
            var claimed = new HashSet<Guid>();
            foreach (var itemId in normalizedItemIds)
            {
                if (await TryClaimItemAsync(
                        batchId,
                        itemId,
                        normalizedWorkerId,
                        now,
                        leaseDuration,
                        cancellationToken) is not null)
                {
                    claimed.Add(itemId);
                }
            }

            return claimed;
        }

        var leaseUntil = now.Add(leaseDuration);
        await dbContext.GradingItems
            .Where(item => normalizedItemIds.Contains(item.Id) &&
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

        return await dbContext.GradingItems
            .AsNoTracking()
            .Where(item => normalizedItemIds.Contains(item.Id) &&
                           item.BatchId == batchId &&
                           item.LeaseOwner == normalizedWorkerId &&
                           item.LeaseUntil != null && item.LeaseUntil > now)
            .Select(item => item.Id)
            .ToHashSetAsync(cancellationToken);
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

    public async Task<int> RenewItemLeasesAsync(
        Guid batchId,
        IReadOnlyCollection<Guid> itemIds,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        ValidateJobArguments(workerId, leaseDuration);
        var normalizedWorkerId = workerId.Trim();
        var normalizedItemIds = itemIds
            .Where(itemId => itemId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (normalizedItemIds.Length == 0)
        {
            return 0;
        }

        if (IsInMemory)
        {
            var renewed = 0;
            foreach (var itemId in normalizedItemIds)
            {
                if (await RenewItemLeaseAsync(
                        batchId,
                        itemId,
                        normalizedWorkerId,
                        now,
                        leaseDuration,
                        cancellationToken))
                {
                    renewed++;
                }
            }

            return renewed;
        }

        return await dbContext.GradingItems
            .Where(item => normalizedItemIds.Contains(item.Id) &&
                           item.BatchId == batchId &&
                           item.Status == GradingItemStatus.Pending &&
                           item.LeaseOwner == normalizedWorkerId &&
                           item.LeaseUntil != null && item.LeaseUntil > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseUntil, now.Add(leaseDuration))
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
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

    public async Task<int> ReleaseItemLeasesAsync(
        Guid batchId,
        IReadOnlyCollection<Guid> itemIds,
        string workerId,
        DateTimeOffset now,
        string? errorCode,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty || string.IsNullOrWhiteSpace(workerId))
        {
            return 0;
        }

        var normalizedItemIds = itemIds
            .Where(itemId => itemId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (normalizedItemIds.Length == 0)
        {
            return 0;
        }

        var normalizedWorkerId = workerId.Trim();
        var normalizedErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : errorCode.Trim()[..Math.Min(120, errorCode.Trim().Length)];

        if (IsInMemory)
        {
            var released = 0;
            foreach (var itemId in normalizedItemIds)
            {
                if (await ReleaseItemLeaseAsync(
                        batchId,
                        itemId,
                        normalizedWorkerId,
                        now,
                        normalizedErrorCode,
                        nextAttemptAt,
                        cancellationToken))
                {
                    released++;
                }
            }

            return released;
        }

        return await dbContext.GradingItems
            .Where(item => normalizedItemIds.Contains(item.Id) &&
                           item.BatchId == batchId &&
                           item.LeaseOwner == normalizedWorkerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.LastErrorCode, normalizedErrorCode)
                .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

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
