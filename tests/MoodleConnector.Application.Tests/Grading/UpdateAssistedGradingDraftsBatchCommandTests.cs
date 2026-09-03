using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class UpdateAssistedGradingDraftsBatchCommandTests
{
    [Fact]
    public async Task Handle_saves_multiple_feedback_only_reviews_in_one_unit_of_work()
    {
        var repository = new FakeRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, 2);
        var first = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 1);
        var second = AssistedGradingItem.Create(batch.Id, 10, 501, 9002, 102, 1);
        first.SetDraft(null, 0m, "Rascunho 1");
        second.SetDraft(null, 0m, "Rascunho 2");
        repository.Batches.Add(batch);
        repository.Items.AddRange([first, second]);
        repository.Snapshots.AddRange([Snapshot(first), Snapshot(second)]);

        var audit = new FakeAuditRepository();
        var result = await CreateHandler(repository, audit).Handle(
            new UpdateAssistedGradingDraftsBatchCommand(
                batch.Id,
                [
                    new(first.Id, null, "Feedback final 1", "approved"),
                    new(second.Id, null, "Feedback final 2", "approved")
                ]),
            CancellationToken.None);

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal([first.Id, second.Id], result.SavedIds);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Equal(2, audit.Logs.Count);
        Assert.All(repository.Items, item =>
        {
            Assert.Equal(GradingReviewStatus.Reviewed, item.ReviewStatus);
            Assert.Equal(GradingItemStatus.ReadyToCommit, item.Status);
            Assert.Null(item.FinalGrade);
        });
    }

    [Fact]
    public async Task Handle_blocks_numeric_review_without_local_scale_snapshot()
    {
        var repository = new FakeRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 1);
        item.SetDraft(8m, 0.8m, "Rascunho");
        repository.Batches.Add(batch);
        repository.Items.Add(item);

        var result = await CreateHandler(repository, new FakeAuditRepository()).Handle(
            new UpdateAssistedGradingDraftsBatchCommand(
                batch.Id,
                [new(item.Id, 8m, "Feedback", "approved")]),
            CancellationToken.None);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Contains("contexto", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GradingReviewStatus.NotReviewed, item.ReviewStatus);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_rejects_stale_hash_without_overwriting_existing_review()
    {
        var repository = new FakeRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 1);
        item.SetDraft(null, 0m, "Rascunho");
        item.ApplyTeacherReview(null, "Feedback original", "teacher-1", 321, "approved");
        repository.Batches.Add(batch);
        repository.Items.Add(item);
        repository.Snapshots.Add(Snapshot(item));

        var result = await CreateHandler(repository, new FakeAuditRepository()).Handle(
            new UpdateAssistedGradingDraftsBatchCommand(
                batch.Id,
                [new(item.Id, null, "Feedback concorrente", "approved", ExpectedDraftVersionHash: "old-hash", ExpectedReviewStatus: "NotReviewed")]),
            CancellationToken.None);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Contains("alterado", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Feedback original", item.FinalFeedback);
    }

    private static UpdateAssistedGradingDraftsBatchCommandHandler CreateHandler(
        FakeRepository repository,
        FakeAuditRepository audit) =>
        new(
            repository,
            new FakeCurrentUser("teacher-1"),
            new FakeMoodleUserResolver(321),
            audit);

    private sealed class FakeRepository : IGradingReviewRepository
    {
        public List<AssistedGradingBatch> Batches { get; } = [];
        public List<AssistedGradingItem> Items { get; } = [];
        public List<GradingContextSnapshotDocument> Snapshots { get; } = [];
        public int SaveChangesCount { get; private set; }

        public Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken) { Batches.Add(batch); return Task.CompletedTask; }
        public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Batches.SingleOrDefault(batch => batch.Id == id));
        public Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken) { Items.Add(item); return Task.CompletedTask; }
        public Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(Guid batchId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingItem>>(Items.Where(item => item.BatchId == batchId).ToArray());
        public Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken) => Task.FromResult(Items.Count(item => item.BatchId == batchId));
        public Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(Guid gradingItemId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GradingArtifact>>([]);
        public Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(Guid gradingItemId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GradingEvidence>>([]);
        public Task<IReadOnlyDictionary<Guid, GradingContextSnapshotDocument>> ListLatestContextSnapshotsByItemsAsync(IReadOnlyCollection<Guid> gradingItemIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, GradingContextSnapshotDocument>>(Snapshots
                .Where(snapshot => gradingItemIds.Contains(snapshot.GradingItemId))
                .GroupBy(snapshot => snapshot.GradingItemId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(snapshot => snapshot.Version).First()));
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(GradingBatchStatus status, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>([]);
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) { SaveChangesCount++; return Task.CompletedTask; }
    }

    private static GradingContextSnapshotDocument Snapshot(AssistedGradingItem item) =>
        GradingContextSnapshotDocument.FromSnapshot(GradingContextSnapshot.Create(
            item.Id,
            item.BatchId,
            new MoodleAssignmentReference(item.CourseId, item.AssignmentId, null),
            new MoodleSubmissionReference(item.SubmissionId ?? 1),
            new MoodleUserReference(item.MoodleUserId),
            item.AttemptNumber,
            1,
            $"Tarefa {item.AssignmentId}",
            null,
            [],
            null,
            new GradingScaleSnapshot(10m, null, null),
            [],
            [],
            new GradingExtractionSummary("succeeded", 0, false, 0, 0, null),
            new GradingEvidenceCoverage(0, 0, 0, 0, 0, 0, false),
            null,
            [],
            [],
            false));

    private sealed class FakeCurrentUser(string subject) : ICurrentUserContext
    {
        public string Subject { get; } = subject;
        public string? Email => "teacher@example.com";
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasScope(string scope) => false;
    }

    private sealed class FakeMoodleUserResolver(long moodleUserId) : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken) => Task.FromResult<long?>(moodleUserId);
    }

    private sealed class FakeAuditRepository : IMoodleAuditLogRepository
    {
        public List<MoodleAuditLog> Logs { get; } = [];
        public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken) { Logs.Add(log); return Task.CompletedTask; }
        public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(string correlationId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);
        public Task<int> CountByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(Guid batchJobId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);
        public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
