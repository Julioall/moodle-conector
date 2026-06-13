using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class AssistedGradingBatchCommandHandlerTests
{
    [Fact]
    public async Task CreateBatch_CriaLoteComItensAguardandoCorrecao()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository());

        var result = await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: [],
                MaxItems: 25,
                OnlyAwaitingGrading: true),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.BatchJobId);
        Assert.Equal("10", result.CourseId);
        Assert.Equal(["501"], result.AssignmentIds);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.AcceptedItems);
        Assert.Equal(0, result.BlockedItems);
        Assert.Single(repository.Batches);
        Assert.Equal(2, repository.Items.Count);
        Assert.All(repository.Items, item => Assert.Equal(GradingItemStatus.Pending, item.Status));
        Assert.Equal(AssignmentSubmissionFilter.NeedsGrading, mediator.LastListQuery!.Filter);
    }

    [Fact]
    public async Task CreateBatch_RespeitaMaxItemsESubmissionIds()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository());

        var result = await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: ["9002"],
                MaxItems: 1,
                OnlyAwaitingGrading: true),
            CancellationToken.None);

        Assert.Equal(1, result.AcceptedItems);
        var item = Assert.Single(repository.Items);
        Assert.Equal(9002, item.SubmissionId);
        Assert.Equal(102, item.MoodleUserId);
    }

    [Fact]
    public async Task GetBatchStatus_RetornaResumoPaginado()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        var sut = new GetAssistedGradingBatchStatusQueryHandler(repository);

        var result = await sut.Handle(
            new GetAssistedGradingBatchStatusQuery(batch.Id, Page: 1, PageSize: 10),
            CancellationToken.None);

        Assert.Equal(batch.Id, result.BatchJobId);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(1, result.TotalItems);
        var statusItem = Assert.Single(result.Items);
        Assert.Equal(item.Id, statusItem.GradingItemId);
        Assert.Equal("DraftReady", statusItem.Status);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task GetItem_RetornaDetalheMinimoDaCorrecao()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        item.ApplyTeacherReview(8.5m, "Feedback final revisado.", "teacher-1", 321);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        var sut = new GetAssistedGradingItemQueryHandler(repository);

        var result = await sut.Handle(
            new GetAssistedGradingItemQuery(item.Id, batch.Id),
            CancellationToken.None);

        Assert.Equal(item.Id, result.GradingItemId);
        Assert.Equal(batch.Id, result.BatchJobId);
        Assert.Equal("501", result.AssignmentId);
        Assert.Equal("9001", result.SubmissionId);
        Assert.Equal("101", result.StudentId);
        Assert.Equal(8m, result.SuggestedGrade);
        Assert.Equal(8.5m, result.FinalGrade);
        Assert.Equal("Feedback final revisado.", result.FinalFeedback);
        Assert.Equal("Reviewed", result.ReviewStatus);
    }

    [Fact]
    public async Task GetItem_QuandoBatchInformadoNaoCorresponde_DeveFalhar()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new GetAssistedGradingItemQueryHandler(repository);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Handle(
                new GetAssistedGradingItemQuery(item.Id, Guid.Parse("00000000-0000-0000-0000-000000000999")),
                CancellationToken.None));

        Assert.Equal("O item informado nao pertence ao lote solicitado.", ex.Message);
    }

    [Fact]
    public async Task UpdateDraft_SalvaDecisaoProfessorERevisaoFinal()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new UpdateAssistedGradingDraftCommandHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository());

        var result = await sut.Handle(
            new UpdateAssistedGradingDraftCommand(
                item.Id,
                FinalGrade: 8.5m,
                FinalFeedback: "Feedback final revisado.",
                TeacherDecision: "approved",
                ReviewNotes: "Ajustei a nota pela conclusao.",
                ExpectedReviewStatus: "NotReviewed"),
            CancellationToken.None);

        Assert.Equal(item.Id, result.GradingItemId);
        Assert.Equal(8.5m, result.FinalGrade);
        Assert.Equal("Feedback final revisado.", result.FinalFeedback);
        Assert.Equal("approved", result.TeacherDecision);
        Assert.Equal("Ajustei a nota pela conclusao.", result.ReviewNotes);
        Assert.Equal("Reviewed", result.ReviewStatus);
        Assert.Equal("Pending", result.CommitStatus);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateDraft_RepetidoComMesmoPayload_RetornaResultadoSemDuplicarAlteracao()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        item.ApplyTeacherReview(8.5m, "Feedback final revisado.", "teacher-1", 321, "approved", "Ajustei a nota pela conclusao.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new UpdateAssistedGradingDraftCommandHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository());

        var result = await sut.Handle(
            new UpdateAssistedGradingDraftCommand(
                item.Id,
                FinalGrade: 8.5m,
                FinalFeedback: "Feedback final revisado.",
                TeacherDecision: "approved",
                ReviewNotes: "Ajustei a nota pela conclusao.",
                ExpectedReviewStatus: "NotReviewed"),
            CancellationToken.None);

        Assert.Equal("Reviewed", result.ReviewStatus);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateDraft_ComStatusEsperadoDivergenteEBpayloadDiferente_BloqueiaSobrescrita()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        item.ApplyTeacherReview(8.5m, "Feedback final revisado.", "teacher-1", 321, "approved", "Ajustei a nota.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new UpdateAssistedGradingDraftCommandHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Handle(
                new UpdateAssistedGradingDraftCommand(
                    item.Id,
                    FinalGrade: 7m,
                    FinalFeedback: "Outro feedback.",
                    TeacherDecision: "needs_changes",
                    ReviewNotes: "Mudanca concorrente.",
                    ExpectedReviewStatus: "NotReviewed"),
                CancellationToken.None));

        Assert.Equal("O rascunho foi alterado desde a ultima leitura. Consulte o item novamente antes de sobrescrever.", ex.Message);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    private sealed class FakeGradingReviewRepository : IGradingReviewRepository
    {
        public List<AssistedGradingBatch> Batches { get; } = [];

        public List<AssistedGradingItem> Items { get; } = [];

        public int SaveChangesCount { get; private set; }

        public Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }

        public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Batches.SingleOrDefault(batch => batch.Id == id));
        }

        public Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        }

        public Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
            Guid batchId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Items
                .Where(item => item.BatchId == batchId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<AssistedGradingItem>>(items);
        }

        public Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.Count(item => item.BatchId == batchId));
        }

        public Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(
            Guid gradingItemId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GradingArtifact>>([]);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUserContext(string subject) : ICurrentUserContext
    {
        public string Subject { get; } = subject;
        public string? Email => "teacher@example.com";
        public IReadOnlyCollection<string> Scopes => [];

        public bool HasScope(string scope)
        {
            return false;
        }
    }

    private sealed class FakeMoodleUserResolver(long? moodleUserId) : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(moodleUserId);
        }
    }

    private sealed class FakeAuditLogRepository : IMoodleAuditLogRepository
    {
        public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(
            string correlationId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);

        public Task<int> CountByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(
            Guid batchJobId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);

        public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeMediator : IMediator
    {
        public ListAssignmentSubmissionsQuery? LastListQuery { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ListAssignmentSubmissionsQuery list)
            {
                LastListQuery = list;
                return Task.FromResult((TResponse)(object)CreatePage(list));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is ListAssignmentSubmissionsQuery list)
            {
                LastListQuery = list;
                return Task.FromResult<object?>(CreatePage(list));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();

        private static AssignmentSubmissionsPage CreatePage(ListAssignmentSubmissionsQuery query)
        {
            return new AssignmentSubmissionsPage(
                "10",
                query.AssignmentId,
                "42",
                "Tarefa 1",
                query.Page,
                query.PageSize,
                query.Filter,
                query.IncludeLate,
                query.IncludeUngraded,
                query.Since,
                query.Before,
                Total: 2,
                HasMore: false,
                [
                    new AssignmentSubmissionSummary(
                        "101",
                        "Ana Souza",
                        "9001",
                        "submitted",
                        "notgraded",
                        Submitted: true,
                        Late: false,
                        NeedsGrading: true,
                        SubmittedAt: new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero),
                        ModifiedAt: new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero),
                        AttemptNumber: 0,
                        FileCount: 1,
                        HasOnlineText: true),
                    new AssignmentSubmissionSummary(
                        "102",
                        "Bruno Lima",
                        "9002",
                        "submitted",
                        "notgraded",
                        Submitted: true,
                        Late: false,
                        NeedsGrading: true,
                        SubmittedAt: new DateTimeOffset(2026, 6, 10, 11, 0, 0, TimeSpan.Zero),
                        ModifiedAt: new DateTimeOffset(2026, 6, 10, 11, 0, 0, TimeSpan.Zero),
                        AttemptNumber: 0,
                        FileCount: 0,
                        HasOnlineText: true)
                ]);
        }
    }
}
