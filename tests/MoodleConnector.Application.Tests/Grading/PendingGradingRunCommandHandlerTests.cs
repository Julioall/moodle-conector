using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Courses;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class PendingGradingRunCommandHandlerTests
{
    [Fact]
    public async Task StartRun_ContinuaNosDemaisCursosQuandoUmCursoFalha()
    {
        var mediator = new RunMediator();
        var sut = new StartPendingGradingRunCommandHandler(
            mediator,
            new RunCourseContentsGateway());

        var result = await sut.Handle(
            new StartPendingGradingRunCommand(
                UserExternalId: "321",
                MaxCourses: 10,
                MaxItemsPerBatch: 100),
            CancellationToken.None);

        var batch = Assert.Single(result.Batches);
        Assert.Equal("10", batch.CourseId);
        Assert.Equal(2, result.CoursesScanned);
        Assert.Equal(1, result.CoursesWithPendingSubmissions);
        Assert.Equal(2, result.TotalItems);
        Assert.Contains(result.Courses, course =>
            course.CourseId == "20" && course.Status == "course_read_failed");
        Assert.Contains(result.Warnings, warning => warning.Contains("Curso 20", StringComparison.Ordinal));
        Assert.Single(mediator.CreateBatchRequests);
        Assert.Equal("10", mediator.CreateBatchRequests[0].CourseId);
    }

    [Fact]
    public async Task StartRun_DivideEntregasDeUmaAtividadeEmSublotesSemPerderItens()
    {
        var mediator = new RunMediator(pendingSubmissionCount: 3);
        var sut = new StartPendingGradingRunCommandHandler(
            mediator,
            new RunCourseContentsGateway());

        var result = await sut.Handle(
            new StartPendingGradingRunCommand("321", MaxCourses: 10, MaxItemsPerBatch: 2),
            CancellationToken.None);

        Assert.Equal(2, result.Batches.Count);
        Assert.Equal(3, result.TotalItems);
        Assert.All(mediator.CreateBatchRequests, request =>
            Assert.InRange(request.PrefetchedSubmissions!.Count, 1, 2));
    }

    [Fact]
    public async Task GetRunReport_ListaCorrigidosENaoCorrigidosComMotivo()
    {
        var repository = new RunRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 2);
        var corrected = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        corrected.SetDraft(9m, 0.9m, "Rascunho aprovado.");
        corrected.ApplyTeacherReview(9m, "Feedback final.", "teacher-1", 321);
        corrected.MarkCommitSucceeded();
        var unreadable = AssistedGradingItem.Create(batch.Id, 10, 501, 9002, 102, 0);
        unreadable.BlockAnalysis("Submissao sem conteudo legivel: arquivo PDF corrompido.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(corrected, CancellationToken.None);
        await repository.AddItemAsync(unreadable, CancellationToken.None);

        var sut = new GetPendingGradingRunReportQueryHandler(
            repository,
            new RunCurrentUserContext("teacher-1"));

        var result = await sut.Handle(
            new GetPendingGradingRunReportQuery([batch.Id]),
            CancellationToken.None);

        Assert.Equal(2, result.TotalItems);
        Assert.Equal(1, result.CorrectedCount);
        Assert.Equal(1, result.NotCorrectedCount);
        Assert.Equal(corrected.Id, Assert.Single(result.CorrectedItems).GradingItemId);
        var blocked = Assert.Single(result.NotCorrectedItems);
        Assert.Equal(unreadable.Id, blocked.GradingItemId);
        Assert.Contains("arquivo PDF corrompido", blocked.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Corrigidas e lancadas no Moodle", result.ReportMarkdown, StringComparison.Ordinal);
        Assert.Contains("Nao corrigidas para ajuste manual", result.ReportMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAiBatch_MantemBloqueadosForaDaCorrecaoEmCadeia()
    {
        var repository = new RunRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 2);
        var blocked = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        blocked.BlockAnalysis("Arquivo enviado esta corrompido e nao pode ser lido.");
        var eligible = AssistedGradingItem.Create(batch.Id, 10, 501, 9002, 102, 0);
        eligible.MarkAwaitingAiAnalysis("Pre-validacao concluida.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(blocked, CancellationToken.None);
        await repository.AddItemAsync(eligible, CancellationToken.None);
        var sut = new SaveAiGradingBatchCommandHandler(
            repository,
            new RunCurrentUserContext("teacher-1"),
            new RunMoodleUserResolver(),
            new RunAuditLogRepository());

        var result = await sut.Handle(
            new SaveAiGradingBatchCommand(
                batch.Id,
                [
                    new AiGradingItemInput(blocked.Id, "Aluno bloqueado", 8m, "Feedback que deve ser ignorado."),
                    new AiGradingItemInput(eligible.Id, "Aluno apto", 9m, "Feedback gerado para revisao.")
                ]),
            CancellationToken.None);

        Assert.Equal(1, result.SavedItems);
        Assert.Equal(1, result.SkippedItems);
        Assert.Equal(GradingItemStatus.Blocked, blocked.Status);
        Assert.Contains("corrompido", blocked.DraftFeedback, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GradingItemStatus.DraftReady, eligible.Status);
    }

    [Fact]
    public async Task PrepareAiBatch_ExcluiItensBloqueadosDoPacoteDeCorrecao()
    {
        var repository = new RunRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 2);
        var blocked = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        blocked.BlockAnalysis("Arquivo sem conteudo legivel.");
        var eligible = AssistedGradingItem.Create(batch.Id, 10, 501, 9002, 102, 0);
        eligible.MarkAwaitingAiAnalysis("Pre-validacao concluida.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(blocked, CancellationToken.None);
        await repository.AddItemAsync(eligible, CancellationToken.None);
        var sut = new PrepareAiGradingBatchQueryHandler(
            repository,
            new RunCurrentUserContext("teacher-1"),
            new RunAssignmentSettingsGateway());

        var result = await sut.Handle(new PrepareAiGradingBatchQuery(batch.Id), CancellationToken.None);

        Assert.Equal(1, result.TotalItems);
        Assert.Equal(eligible.Id, Assert.Single(result.Items).GradingItemId);
        Assert.Contains(result.Warnings, warning => warning.Contains("bloqueado", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RunCurrentUserContext(string subject) : ICurrentUserContext
    {
        public string Subject { get; } = subject;
        public string? Email => "teacher@example.com";
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasScope(string scope) => false;
    }

    private sealed class RunMoodleUserResolver : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken) => Task.FromResult<long?>(321);
    }

    private sealed class RunAuditLogRepository : IMoodleAuditLogRepository
    {
        public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(string correlationId, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);
        public Task<int> CountByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(Guid batchJobId, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);
        public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RunAssignmentSettingsGateway : IMoodleAssignmentSettingsGateway
    {
        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            string assignmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AssignmentSettingsSummary?>(new AssignmentSettingsSummary(assignmentId, 10m, "Atividade avaliativa"));
    }

    private sealed class RunCourseContentsGateway : IMoodleCourseContentsGateway
    {
        public Task<CourseContentsSummary> GetCourseContentsAsync(
            string userExternalId,
            string courseId,
            IReadOnlyCollection<string> moduleTypes,
            bool includeHidden,
            bool onlyWithFiles,
            CancellationToken cancellationToken)
        {
            if (courseId == "20")
            {
                throw new InvalidOperationException("Moodle indisponivel para o curso.");
            }

            var assignment = new CourseModuleSummary(
                "1001",
                "501",
                "assign",
                "Atividade avaliativa",
                null,
                Visible: true,
                UserVisible: true,
                Description: null,
                AvailabilityInfo: null,
                Dates: [],
                Files: []);
            return Task.FromResult(new CourseContentsSummary(
                courseId,
                moduleTypes.ToArray(),
                includeHidden,
                onlyWithFiles,
                [new CourseSectionSummary("1", 1, "Topico", null, true, 1, false, [assignment])]));
        }
    }

    private sealed class RunMediator(int pendingSubmissionCount = 2) : IMediator
    {
        public List<CreateAssistedGradingBatchCommand> CreateBatchRequests { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ListMyCoursesQuery)
            {
                return Task.FromResult((TResponse)(object)new PagedCourses(
                    [Course("10", "Curso valido"), Course("20", "Curso indisponivel")],
                    TotalCount: 2,
                    Page: 1,
                    PageSize: 10));
            }

            if (request is CreateAssistedGradingBatchCommand create)
            {
                CreateBatchRequests.Add(create);
                var itemCount = create.PrefetchedSubmissions?.Count ?? 0;
                return Task.FromResult((TResponse)(object)new CreateAssistedGradingBatchResult(
                    Guid.Parse($"00000000-0000-0000-0000-{100 + CreateBatchRequests.Count:D12}"),
                    create.CourseId,
                    create.AssignmentIds,
                    TotalItems: itemCount,
                    AcceptedItems: itemCount,
                    BlockedItems: 0,
                    Status: "ReadyForReview",
                    Warnings: []));
            }

            if (request is ListAssignmentSubmissionsQuery submissions)
            {
                return Task.FromResult((TResponse)(object)new AssignmentSubmissionsPage(
                    submissions.CourseId,
                    submissions.AssignmentId,
                    AssignmentModuleId: "1001",
                    AssignmentName: "Atividade avaliativa",
                    submissions.Page,
                    submissions.PageSize,
                    submissions.Filter,
                    submissions.IncludeLate,
                    submissions.IncludeUngraded,
                    submissions.Since,
                    submissions.Before,
                    Total: pendingSubmissionCount,
                    HasMore: false,
                    Enumerable.Range(1, pendingSubmissionCount)
                        .Select(index => Submission((100 + index).ToString(), (9000 + index).ToString()))
                        .ToArray()));
            }

            throw new NotSupportedException($"Request nao suportado: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException($"Request nao suportado: {request.GetType().Name}");
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<object?>();

        private static CourseSummary Course(string id, string name) => new(
            id,
            IdNumber: null,
            ShortName: name,
            FullName: name,
            DisplayName: name,
            CategoryId: null,
            CategoryName: null,
            StartDate: null,
            EndDate: null,
            Visible: true,
            ViewUrl: null,
            CourseImage: null,
            Progress: null,
            HasProgress: null,
            IsFavourite: null,
            LastAccessAt: null);

        private static AssignmentSubmissionSummary Submission(string userId, string submissionId) => new(
            userId,
            FullName: null,
            SubmissionId: submissionId,
            Status: "submitted",
            GradingStatus: "notgraded",
            Submitted: true,
            Late: false,
            NeedsGrading: true,
            SubmittedAt: null,
            ModifiedAt: null,
            AttemptNumber: 0,
            FileCount: 1,
            HasOnlineText: false,
            Files: []);
    }

    private sealed class RunRepository : IGradingReviewRepository
    {
        private readonly List<AssistedGradingBatch> _batches = [];
        private readonly List<AssistedGradingItem> _items = [];

        public Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken)
        {
            _batches.Add(batch);
            return Task.CompletedTask;
        }

        public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_batches.SingleOrDefault(batch => batch.Id == id));

        public Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken)
        {
            _items.Add(item);
            return Task.CompletedTask;
        }

        public Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
            Guid batchId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssistedGradingItem>>(_items
                .Where(item => item.BatchId == batchId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray());

        public Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.Count(item => item.BatchId == batchId));
        public Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(Guid gradingItemId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GradingArtifact>>([]);
        public Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(Guid gradingItemId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GradingEvidence>>([]);
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(GradingBatchStatus status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssistedGradingBatch>>([]);
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssistedGradingBatch>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
