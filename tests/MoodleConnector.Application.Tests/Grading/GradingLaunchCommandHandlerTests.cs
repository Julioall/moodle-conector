using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class GradingLaunchCommandHandlerTests
{
    [Fact]
    public async Task CreatePreview_CriaPendingActionComItensRevisados()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var sut = new CreateGradingLaunchPreviewCommandHandler(
            fixture.GradingRepository,
            fixture.PendingActions);

        var result = await sut.Handle(
            new CreateGradingLaunchPreviewCommand(
                batch.Id,
                GradingItemIds: [],
                OnlyReviewed: true),
            CancellationToken.None);

        Assert.Equal(fixture.PendingActions.PendingActionId, result.PendingActionId);
        Assert.Equal(batch.Id, result.BatchJobId);
        Assert.Equal(1, result.ReadyItems);
        Assert.Equal(0, result.BlockedItems);
        Assert.Equal("CONFIRMAR LANCAMENTO 1 ITEM", result.ConfirmationText);
        Assert.NotNull(fixture.PendingActions.LastPayload);
        Assert.Single(fixture.PendingActions.LastPayload!.Items);
        Assert.Equal("501", fixture.PendingActions.LastPayload.Items[0].AssignmentId);
        Assert.Equal("101", fixture.PendingActions.LastPayload.Items[0].StudentId);
        Assert.Equal(8.5m, fixture.PendingActions.LastPayload.Items[0].Grade);
        Assert.Equal("Feedback final revisado.", fixture.PendingActions.LastPayload.Items[0].FeedbackText);
    }

    [Fact]
    public async Task CreatePreview_BloqueiaItemSemRevisao()
    {
        var fixture = new Fixture();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await fixture.GradingRepository.AddBatchAsync(batch, CancellationToken.None);
        await fixture.GradingRepository.AddItemAsync(item, CancellationToken.None);
        var sut = new CreateGradingLaunchPreviewCommandHandler(
            fixture.GradingRepository,
            fixture.PendingActions);

        var result = await sut.Handle(
            new CreateGradingLaunchPreviewCommand(
                batch.Id,
                GradingItemIds: [],
                OnlyReviewed: true),
            CancellationToken.None);

        Assert.Equal(Guid.Empty, result.PendingActionId);
        Assert.Equal(0, result.ReadyItems);
        Assert.Equal(1, result.BlockedItems);
        Assert.Null(fixture.PendingActions.LastPayload);
        Assert.Contains(result.Warnings, warning => warning.Contains("Nenhum item revisado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConfirmLaunch_ConfirmaAcaoEEnviaNotasIndividuais()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal("confirmed", result.Status);
        Assert.Equal(1, result.SentItems);
        Assert.Equal(0, result.FailedItems);
        Assert.Single(fixture.Mediator.SavedGrades);
        Assert.Equal("501", fixture.Mediator.SavedGrades[0].AssignmentId);
        Assert.Equal("101", fixture.Mediator.SavedGrades[0].StudentId);
        Assert.Equal(GradingCommitStatus.Succeeded, item.CommitStatus);
        Assert.Equal("moodle.write", fixture.Confirmations.LastRequiredScope);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_succeeded");
        Assert.Equal("confirmar_lancamento_lote_moodle", auditLog.ToolName);
        Assert.Equal("mod_assign_save_grade", auditLog.MoodleFunction);
        Assert.Equal("audit-1", auditLog.CorrelationId);
        Assert.Contains(item.Id.ToString(), auditLog.RequestSanitizedJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmLaunch_Repetido_NaoReenviaItemJaEnviado()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        item.MarkCommitSucceeded();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoFuncaoMoodleDeEscritaNaoEstaDisponivel()
    {
        var fixture = new Fixture();
        fixture.Capabilities.Functions.Clear();
        fixture.Capabilities.Functions.Add("mod_assign_get_submissions");
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal("confirmed", result.Status);
        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("mod_assign_save_grade", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_function_unavailable", auditLog.ErrorCode);
        Assert.Equal("mod_assign_save_grade", auditLog.MoodleFunction);
    }

    private sealed class Fixture
    {
        public FakeGradingReviewRepository GradingRepository { get; } = new();
        public FakePendingActionService PendingActions { get; } = new();
        public FakePendingActionRepository PendingRepository { get; } = new();
        public FakeActionConfirmationService Confirmations { get; } = new();
        public FakeMoodleGradingCapabilitiesGateway Capabilities { get; } = new();
        public FakeAuditLogRepository AuditLogs { get; } = new();
        public FakeMediator Mediator { get; } = new();

        public AssistedGradingBatch CreateBatchWithReviewedItem()
        {
            var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
            var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
            item.SetDraft(8m, 0.8m, "Rascunho.");
            item.ApplyTeacherReview(8.5m, "Feedback final revisado.", "teacher-1", 321, "approved", "ok");
            GradingRepository.Batches.Add(batch);
            GradingRepository.Items.Add(item);
            return batch;
        }

        public PendingMoodleAction CreatePendingLaunchAction(Guid batchId, Guid gradingItemId)
        {
            var payload = new GradingLaunchPayload(
                batchId,
                [
                    new GradingLaunchPayloadItem(
                        gradingItemId,
                        "10",
                        "501",
                        "101",
                        8.5m,
                        "Feedback final revisado.",
                        AttemptNumber: 0)
                ]);

            return new PendingMoodleAction
            {
                Id = PendingActions.PendingActionId,
                ToolName = "criar_previa_lancamento_lote",
                RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
                CreatedBySubject = "teacher-1",
                CourseId = 10,
                PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PreviewJson = "{}",
                ConfirmationText = "CONFIRMAR LANCAMENTO 1 ITEM",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                IdempotencyKey = "idem-1",
                CorrelationId = "audit-1"
            };
        }
    }

    private sealed class FakeGradingReviewRepository : IGradingReviewRepository
    {
        public List<AssistedGradingBatch> Batches { get; } = [];
        public List<AssistedGradingItem> Items { get; } = [];

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

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakePendingActionService : IPendingActionService
    {
        public Guid PendingActionId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000999");
        public GradingLaunchPayload? LastPayload { get; private set; }

        public Task<PendingActionResponse> CreatePendingActionAsync(
            string toolName,
            ToolRiskLevel riskLevel,
            object payload,
            object preview,
            string confirmationText,
            TimeSpan expiresIn,
            long? courseId,
            CancellationToken cancellationToken)
        {
            LastPayload = Assert.IsType<GradingLaunchPayload>(payload);
            return Task.FromResult(new PendingActionResponse(
                "pending_confirmation",
                PendingActionId,
                toolName,
                riskLevel,
                preview,
                confirmationText,
                DateTimeOffset.UtcNow.Add(expiresIn)));
        }
    }

    private sealed class FakePendingActionRepository : IPendingMoodleActionRepository
    {
        public List<PendingMoodleAction> Actions { get; } = [];

        public Task AddAsync(PendingMoodleAction action, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }

        public Task<PendingMoodleAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Actions.SingleOrDefault(action => action.Id == id));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeActionConfirmationService : IActionConfirmationService
    {
        public string? LastRequiredScope { get; private set; }

        public Task<ActionConfirmationResponse> ConfirmAsync(
            Guid pendingActionId,
            string confirmationText,
            string? requiredScope,
            CancellationToken cancellationToken)
        {
            LastRequiredScope = requiredScope;
            return Task.FromResult(new ActionConfirmationResponse(
                "confirmed",
                pendingActionId,
                "criar_previa_lancamento_lote",
                ToolRiskLevel.CriticalHumanConfirmedWrite,
                DateTimeOffset.UtcNow,
                "audit-1"));
        }
    }

    private sealed class FakeMoodleGradingCapabilitiesGateway : IMoodleGradingCapabilitiesGateway
    {
        public List<string> Functions { get; } = ["mod_assign_save_grade"];

        public Task<MoodleWebServiceFunctionCatalog> GetFunctionCatalogAsync(
            string userExternalId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new MoodleWebServiceFunctionCatalog(
                "moodle_mobile_app",
                Functions));
        }
    }

    private sealed class FakeAuditLogRepository : IMoodleAuditLogRepository
    {
        public List<MoodleAuditLog> Logs { get; } = [];

        public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(
            string correlationId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Logs
                .Where(log => log.CorrelationId == correlationId)
                .OrderBy(log => log.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MoodleAuditLog>>(items);
        }

        public Task<int> CountByCorrelationIdAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Logs.Count(log => log.CorrelationId == correlationId));
        }

        public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(
            Guid batchJobId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Logs
                .Where(log => log.BatchJobId == batchJobId)
                .OrderBy(log => log.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MoodleAuditLog>>(items);
        }

        public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Logs.Count(log => log.BatchJobId == batchJobId));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMediator : IMediator
    {
        public List<SaveAssignmentGradeCommand> SavedGrades { get; } = [];

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
            if (request is SaveAssignmentGradeCommand save)
            {
                SavedGrades.Add(save);
                return Task.FromResult((TResponse)(object)new AssignmentGradeWriteResult(
                    true,
                    "mod_assign_save_grade",
                    "ok"));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is SaveAssignmentGradeCommand save)
            {
                SavedGrades.Add(save);
                return Task.FromResult<object?>(new AssignmentGradeWriteResult(
                    true,
                    "mod_assign_save_grade",
                    "ok"));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();
    }
}
