using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools.Grading;

namespace MoodleConnector.Application.Tests.Tools.Grading;

public sealed class MoodleGradingToolsTests
{
    [Fact]
    public async Task Deve_descobrir_funcoes_moodle_para_correcao_sem_expor_token()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var sut = new MoodleGradingTools(mediator, selection, new FakeMoodleUserResolver(321));

        var result = await sut.DescobrirFuncoesMoodleCorrecaoAsync("goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);
        Assert.NotNull(mediator.LastQuery);
        Assert.Equal("321", mediator.LastQuery!.UserExternalId);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");

        Assert.True(data.GetProperty("canReadSubmissions").GetBoolean());
        Assert.True(data.GetProperty("canReadGrades").GetBoolean());
        Assert.True(data.GetProperty("canWriteIndividualGrades").GetBoolean());
        Assert.False(data.GetProperty("canWriteBatchGrades").GetBoolean());
        Assert.Contains(
            data.GetProperty("functions").EnumerateArray(),
            function => function.GetProperty("name").GetString() == "mod_assign_save_grade" &&
                function.GetProperty("available").GetBoolean());
        Assert.DoesNotContain("token", data.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deve_retornar_erro_controlado_quando_usuario_moodle_nao_for_identificado()
    {
        var sut = new MoodleGradingTools(
            new FakeMediator(),
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(null));

        var result = await sut.DescobrirFuncoesMoodleCorrecaoAsync();

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("Usuario nao autenticado para descobrir funcoes de correcao.", structured.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task Deve_criar_lote_correcao_assistida()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var sut = new MoodleGradingTools(mediator, selection, new FakeMoodleUserResolver(321));

        var result = await sut.CriarLoteCorrecaoAssistidaAsync(
            "10",
            ["501"],
            maxItems: 25,
            moodleAlias: "goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);
        Assert.NotNull(mediator.LastCreateBatch);
        Assert.Equal("321", mediator.LastCreateBatch!.UserExternalId);
        Assert.Equal("10", mediator.LastCreateBatch.CourseId);
        Assert.Equal(["501"], mediator.LastCreateBatch.AssignmentIds);
        Assert.True(mediator.LastCreateBatch.IncludeRubric);
        Assert.True(mediator.LastCreateBatch.IncludeSubmissionFiles);
        Assert.False(mediator.LastCreateBatch.IncludeCourseMaterials);
        Assert.Equal("normal", mediator.LastCreateBatch.Priority);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("00000000-0000-0000-0000-000000000123", data.GetProperty("batchJobId").GetString());
        Assert.Equal(2, data.GetProperty("acceptedItems").GetInt32());
    }

    [Fact]
    public async Task Deve_listar_entregas_corrigiveis_com_contadores()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var sut = new MoodleGradingTools(mediator, selection, new FakeMoodleUserResolver(321));

        var result = await sut.ListarEntregasCorrigiveisAsync(
            "10",
            ["501"],
            onlyAwaitingGrading: true,
            page: 1,
            perPage: 25,
            moodleAlias: "goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, data.GetProperty("counters").GetProperty("awaitingGrading").GetInt32());
        Assert.Equal(1, data.GetProperty("items").GetArrayLength());
        Assert.Equal("501", data.GetProperty("items")[0].GetProperty("assignmentId").GetString());
    }

    [Fact]
    public async Task Deve_consultar_status_lote_correcao()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleGradingTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(321));

        var result = await sut.ConsultarStatusLoteCorrecaoAsync(
            Guid.Parse("00000000-0000-0000-0000-000000000123"));

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastStatusQuery);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("Pending", data.GetProperty("status").GetString());
        Assert.Equal(1, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Deve_consultar_item_correcao_assistida()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleGradingTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(321));

        var result = await sut.ConsultarItemCorrecaoAssistidaAsync(
            Guid.Parse("00000000-0000-0000-0000-000000000456"),
            Guid.Parse("00000000-0000-0000-0000-000000000123"));

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastItemQuery);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000123"), mediator.LastItemQuery!.BatchJobId);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("00000000-0000-0000-0000-000000000456", data.GetProperty("gradingItemId").GetString());
        Assert.Equal("101", data.GetProperty("studentId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("draftVersionHash").GetString()));
        Assert.Equal(2, data.GetProperty("pendingIssues").GetArrayLength());
        Assert.False(data.TryGetProperty("attachments", out _));
        Assert.False(data.TryGetProperty("studentEmail", out _));
    }

    [Fact]
    public async Task Deve_atualizar_rascunho_correcao()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleGradingTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(321));

        var result = await sut.AtualizarRascunhoCorrecaoAsync(
            Guid.Parse("00000000-0000-0000-0000-000000000456"),
            finalGrade: 8.5m,
            finalFeedback: "Feedback final revisado.",
            teacherDecision: "approved",
            reviewNotes: "Ajustei a nota pela conclusao.",
            expectedReviewStatus: "NotReviewed");

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastUpdateDraft);
        Assert.Equal(8.5m, mediator.LastUpdateDraft!.FinalGrade);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("Reviewed", data.GetProperty("reviewStatus").GetString());
        Assert.Equal("approved", data.GetProperty("teacherDecision").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("draftVersionHash").GetString()));
        Assert.Equal(0, data.GetProperty("pendingIssues").GetArrayLength());
    }

    [Fact]
    public async Task Deve_criar_previa_lancamento_lote()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleGradingTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(321));

        var result = await sut.CriarPreviaLancamentoLoteAsync(
            Guid.Parse("00000000-0000-0000-0000-000000000123"));

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastCreatePreview);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("00000000-0000-0000-0000-000000000999", data.GetProperty("pendingActionId").GetString());
        var confirmationText = data.GetProperty("confirmationText").GetString();
        Assert.StartsWith("CONFIRMO O LANCAMENTO DE 1 CORRECAO NO MOODLE PARA O LOTE", confirmationText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deve_confirmar_lancamento_lote_moodle()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleGradingTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(321));

        var result = await sut.ConfirmarLancamentoLoteMoodleAsync(
            Guid.Parse("00000000-0000-0000-0000-000000000999"),
            "CONFIRMO O LANCAMENTO DE 1 CORRECAO NO MOODLE PARA O LOTE 00000000-0000-0000-0000-000000000123 DO CURSO 10");

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastConfirmLaunch);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal(1, data.GetProperty("sentItems").GetInt32());
        Assert.Equal(0, data.GetProperty("failedItems").GetInt32());
    }

    [Fact]
    public async Task Deve_consultar_auditoria_correcao()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleGradingTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(321));

        var result = await sut.ConsultarAuditoriaCorrecaoAsync("audit-1");

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastAuditQuery);
        Assert.Equal("audit-1", mediator.LastAuditQuery!.AuditId);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalEvents").GetInt32());
        Assert.Equal("commit_succeeded", data.GetProperty("events")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deve_consultar_auditoria_correcao_por_lote()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleGradingTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(321));

        var batchId = Guid.Parse("00000000-0000-0000-0000-000000000123");
        var result = await sut.ConsultarAuditoriaCorrecaoLoteAsync(batchId);

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastBatchAuditQuery);
        Assert.Equal(batchId, mediator.LastBatchAuditQuery!.BatchJobId);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("00000000-0000-0000-0000-000000000123", data.GetProperty("batchJobId").GetString());
        Assert.Equal(1, data.GetProperty("totalEvents").GetInt32());
    }

    private sealed class FakeMoodleConnectionSelection : IMoodleConnectionSelection
    {
        public string? Alias { get; set; }
    }

    private sealed class FakeMoodleUserResolver(long? userId) : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(userId);
        }
    }

    private sealed class FakeMediator : IMediator
    {
        public DiscoverMoodleGradingCapabilitiesQuery? LastQuery { get; private set; }

        public CreateAssistedGradingBatchCommand? LastCreateBatch { get; private set; }

        public GetAssistedGradingBatchStatusQuery? LastStatusQuery { get; private set; }

        public GetAssistedGradingItemQuery? LastItemQuery { get; private set; }

        public UpdateAssistedGradingDraftCommand? LastUpdateDraft { get; private set; }

        public CreateGradingLaunchPreviewCommand? LastCreatePreview { get; private set; }

        public ConfirmMoodleBatchLaunchCommand? LastConfirmLaunch { get; private set; }

        public GetGradingAuditQuery? LastAuditQuery { get; private set; }

        public GetGradingBatchAuditQuery? LastBatchAuditQuery { get; private set; }

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
            if (request is DiscoverMoodleGradingCapabilitiesQuery query)
            {
                LastQuery = query;
                return Task.FromResult((TResponse)(object)CreateReport());
            }

            if (request is CreateAssistedGradingBatchCommand createBatch)
            {
                LastCreateBatch = createBatch;
                return Task.FromResult((TResponse)(object)new CreateAssistedGradingBatchResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "10",
                    ["501"],
                    TotalItems: 2,
                    AcceptedItems: 2,
                    BlockedItems: 0,
                    Status: "Pending",
                    Warnings: []));
            }

            if (request is ListAssignmentSubmissionsQuery)
            {
                return Task.FromResult((TResponse)(object)new AssignmentSubmissionsPage(
                    "10",
                    "501",
                    "42",
                    "Tarefa 1",
                    Page: 1,
                    PageSize: 100,
                    Filter: AssignmentSubmissionFilter.NeedsGrading,
                    IncludeLate: true,
                    IncludeUngraded: true,
                    Since: null,
                    Before: null,
                    Total: 1,
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
                            HasOnlineText: true)
                    ]));
            }

            if (request is GetAssistedGradingBatchStatusQuery statusQuery)
            {
                LastStatusQuery = statusQuery;
                return Task.FromResult((TResponse)(object)new AssistedGradingBatchStatusResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "Pending",
                    TotalItems: 1,
                    ProcessedItems: 0,
                    ReadyItems: 0,
                    BlockedItems: 0,
                    FailedItems: 0,
                    Page: 1,
                    PageSize: 20,
                    HasMore: false,
                    Items:
                    [
                        new AssistedGradingBatchStatusItem(
                            Guid.Parse("00000000-0000-0000-0000-000000000456"),
                            "501",
                            "9001",
                            "101",
                            "Pending",
                            "NotReviewed",
                            "NotReady")
                    ]));
            }

            if (request is GetAssistedGradingItemQuery itemQuery)
            {
                LastItemQuery = itemQuery;
                return Task.FromResult((TResponse)(object)new AssistedGradingItemDetailResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000456"),
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "10",
                    "501",
                    "9001",
                    "101",
                    AttemptNumber: 0,
                    Status: "DraftReady",
                    SuggestedGrade: 8m,
                    FinalGrade: null,
                    Confidence: 0.8m,
                    DraftFeedback: "Rascunho.",
                    FinalFeedback: null,
                    ReviewStatus: "NotReviewed",
                    CommitStatus: "NotReady",
                    TeacherDecision: null,
                    ReviewNotes: null,
                    DraftVersionHash: "hash-item-1",
                    PendingIssues: ["Revisao humana pendente.", "Feedback final pendente."]));
            }

            if (request is UpdateAssistedGradingDraftCommand updateDraft)
            {
                LastUpdateDraft = updateDraft;
                return Task.FromResult((TResponse)(object)new AssistedGradingItemDetailResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000456"),
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "10",
                    "501",
                    "9001",
                    "101",
                    AttemptNumber: 0,
                    Status: "ReadyToCommit",
                    SuggestedGrade: 8m,
                    FinalGrade: 8.5m,
                    Confidence: 0.8m,
                    DraftFeedback: "Rascunho.",
                    FinalFeedback: "Feedback final revisado.",
                    ReviewStatus: "Reviewed",
                    CommitStatus: "Pending",
                    TeacherDecision: "approved",
                    ReviewNotes: "Ajustei a nota pela conclusao.",
                    DraftVersionHash: "hash-item-2",
                    PendingIssues: []));
            }

            if (request is CreateGradingLaunchPreviewCommand createPreview)
            {
                LastCreatePreview = createPreview;
                return Task.FromResult((TResponse)(object)new CreateGradingLaunchPreviewResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000999"),
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    TotalItems: 1,
                    ReadyItems: 1,
                    BlockedItems: 0,
                    Launches:
                    [
                        new GradingLaunchPreviewItem(
                            Guid.Parse("00000000-0000-0000-0000-000000000456"),
                            "501",
                            "101",
                            8.5m,
                            "Feedback final revisado.")
                    ],
                    ConfirmationText: "CONFIRMO O LANCAMENTO DE 1 CORRECAO NO MOODLE PARA O LOTE 00000000-0000-0000-0000-000000000123 DO CURSO 10",
                    ExpiresAt: new DateTimeOffset(2026, 6, 13, 12, 15, 0, TimeSpan.Zero),
                    Warnings: []));
            }

            if (request is ConfirmMoodleBatchLaunchCommand confirmLaunch)
            {
                LastConfirmLaunch = confirmLaunch;
                return Task.FromResult((TResponse)(object)new ConfirmMoodleBatchLaunchResult(
                    "confirmed",
                    Guid.Parse("00000000-0000-0000-0000-000000000999"),
                    SentItems: 1,
                    FailedItems: 0,
                    Failures: [],
                    AuditId: "audit-1"));
            }

            if (request is GetGradingAuditQuery auditQuery)
            {
                LastAuditQuery = auditQuery;
                return Task.FromResult((TResponse)(object)CreateAuditResult());
            }

            if (request is GetGradingBatchAuditQuery batchAuditQuery)
            {
                LastBatchAuditQuery = batchAuditQuery;
                return Task.FromResult((TResponse)(object)CreateBatchAuditResult(batchAuditQuery.BatchJobId));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is DiscoverMoodleGradingCapabilitiesQuery query)
            {
                LastQuery = query;
                return Task.FromResult<object?>(CreateReport());
            }

            if (request is CreateAssistedGradingBatchCommand createBatch)
            {
                LastCreateBatch = createBatch;
                return Task.FromResult<object?>(new CreateAssistedGradingBatchResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "10",
                    ["501"],
                    TotalItems: 2,
                    AcceptedItems: 2,
                    BlockedItems: 0,
                    Status: "Pending",
                    Warnings: []));
            }

            if (request is ListAssignmentSubmissionsQuery)
            {
                return Task.FromResult<object?>(new AssignmentSubmissionsPage(
                    "10",
                    "501",
                    "42",
                    "Tarefa 1",
                    Page: 1,
                    PageSize: 100,
                    Filter: AssignmentSubmissionFilter.NeedsGrading,
                    IncludeLate: true,
                    IncludeUngraded: true,
                    Since: null,
                    Before: null,
                    Total: 1,
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
                            HasOnlineText: true)
                    ]));
            }

            if (request is GetAssistedGradingBatchStatusQuery statusQuery)
            {
                LastStatusQuery = statusQuery;
                return Task.FromResult<object?>(new AssistedGradingBatchStatusResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "Pending",
                    TotalItems: 1,
                    ProcessedItems: 0,
                    ReadyItems: 0,
                    BlockedItems: 0,
                    FailedItems: 0,
                    Page: 1,
                    PageSize: 20,
                    HasMore: false,
                    Items:
                    [
                        new AssistedGradingBatchStatusItem(
                            Guid.Parse("00000000-0000-0000-0000-000000000456"),
                            "501",
                            "9001",
                            "101",
                            "Pending",
                            "NotReviewed",
                            "NotReady")
                    ]));
            }

            if (request is GetAssistedGradingItemQuery itemQuery)
            {
                LastItemQuery = itemQuery;
                return Task.FromResult<object?>(new AssistedGradingItemDetailResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000456"),
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "10",
                    "501",
                    "9001",
                    "101",
                    AttemptNumber: 0,
                    Status: "DraftReady",
                    SuggestedGrade: 8m,
                    FinalGrade: null,
                    Confidence: 0.8m,
                    DraftFeedback: "Rascunho.",
                    FinalFeedback: null,
                    ReviewStatus: "NotReviewed",
                    CommitStatus: "NotReady",
                    TeacherDecision: null,
                    ReviewNotes: null,
                    DraftVersionHash: "hash-item-1",
                    PendingIssues: ["Revisao humana pendente.", "Feedback final pendente."]));
            }

            if (request is UpdateAssistedGradingDraftCommand updateDraft)
            {
                LastUpdateDraft = updateDraft;
                return Task.FromResult<object?>(new AssistedGradingItemDetailResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000456"),
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    "10",
                    "501",
                    "9001",
                    "101",
                    AttemptNumber: 0,
                    Status: "ReadyToCommit",
                    SuggestedGrade: 8m,
                    FinalGrade: 8.5m,
                    Confidence: 0.8m,
                    DraftFeedback: "Rascunho.",
                    FinalFeedback: "Feedback final revisado.",
                    ReviewStatus: "Reviewed",
                    CommitStatus: "Pending",
                    TeacherDecision: "approved",
                    ReviewNotes: "Ajustei a nota pela conclusao.",
                    DraftVersionHash: "hash-item-2",
                    PendingIssues: []));
            }

            if (request is CreateGradingLaunchPreviewCommand createPreview)
            {
                LastCreatePreview = createPreview;
                return Task.FromResult<object?>(new CreateGradingLaunchPreviewResult(
                    Guid.Parse("00000000-0000-0000-0000-000000000999"),
                    Guid.Parse("00000000-0000-0000-0000-000000000123"),
                    TotalItems: 1,
                    ReadyItems: 1,
                    BlockedItems: 0,
                    Launches:
                    [
                        new GradingLaunchPreviewItem(
                            Guid.Parse("00000000-0000-0000-0000-000000000456"),
                            "501",
                            "101",
                            8.5m,
                            "Feedback final revisado.")
                    ],
                    ConfirmationText: "CONFIRMO O LANCAMENTO DE 1 CORRECAO NO MOODLE PARA O LOTE 00000000-0000-0000-0000-000000000123 DO CURSO 10",
                    ExpiresAt: new DateTimeOffset(2026, 6, 13, 12, 15, 0, TimeSpan.Zero),
                    Warnings: []));
            }

            if (request is ConfirmMoodleBatchLaunchCommand confirmLaunch)
            {
                LastConfirmLaunch = confirmLaunch;
                return Task.FromResult<object?>(new ConfirmMoodleBatchLaunchResult(
                    "confirmed",
                    Guid.Parse("00000000-0000-0000-0000-000000000999"),
                    SentItems: 1,
                    FailedItems: 0,
                    Failures: [],
                    AuditId: "audit-1"));
            }

            if (request is GetGradingAuditQuery auditQuery)
            {
                LastAuditQuery = auditQuery;
                return Task.FromResult<object?>(CreateAuditResult());
            }

            if (request is GetGradingBatchAuditQuery batchAuditQuery)
            {
                LastBatchAuditQuery = batchAuditQuery;
                return Task.FromResult<object?>(CreateBatchAuditResult(batchAuditQuery.BatchJobId));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();

        private static MoodleGradingCapabilitiesReport CreateReport()
        {
            return new MoodleGradingCapabilitiesReport(
                "moodle_mobile_app",
                new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero),
                [
                    new MoodleWebServiceFunctionCapability("mod_assign_get_submissions", "read_submissions", true),
                    new MoodleWebServiceFunctionCapability("mod_assign_get_submission_status", "read_submission_status", true),
                    new MoodleWebServiceFunctionCapability("mod_assign_get_grades", "read_grades", true),
                    new MoodleWebServiceFunctionCapability("mod_assign_save_grade", "write_individual_grade", true),
                    new MoodleWebServiceFunctionCapability("mod_assign_save_grades", "write_batch_grades", false),
                    new MoodleWebServiceFunctionCapability("core_files_get_files", "read_files", true)
                ],
                CanReadSubmissions: true,
                CanReadGrades: true,
                CanReadFiles: true,
                CanWriteIndividualGrades: true,
                CanWriteBatchGrades: false,
                MissingFunctions: ["mod_assign_save_grades"]);
        }

        private static GradingAuditResult CreateAuditResult()
        {
            using var request = JsonDocument.Parse("{\"assignmentId\":\"501\",\"studentId\":\"101\"}");
            using var response = JsonDocument.Parse("{\"moodleStatus\":\"ok\"}");
            return new GradingAuditResult(
                "audit-1",
                BatchJobId: null,
                TotalEvents: 1,
                Page: 1,
                PageSize: 20,
                HasMore: false,
                Events:
                [
                    new GradingAuditEvent(
                        new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero),
                        "confirmar_lancamento_lote_moodle",
                        "commit_succeeded",
                        "mod_assign_save_grade",
                        "teacher-1",
                        321,
                        10,
                        ErrorCode: null,
                        ErrorMessage: null,
                        request.RootElement.Clone(),
                        response.RootElement.Clone())
                ]);
        }

        private static GradingAuditResult CreateBatchAuditResult(Guid batchJobId)
        {
            using var request = JsonDocument.Parse("{\"assignmentId\":\"501\",\"studentId\":\"101\"}");
            using var response = JsonDocument.Parse("{\"moodleStatus\":\"ok\"}");
            return new GradingAuditResult(
                AuditId: null,
                BatchJobId: batchJobId,
                TotalEvents: 1,
                Page: 1,
                PageSize: 20,
                HasMore: false,
                Events:
                [
                    new GradingAuditEvent(
                        new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero),
                        "confirmar_lancamento_lote_moodle",
                        "commit_succeeded",
                        "mod_assign_save_grade",
                        "teacher-1",
                        321,
                        10,
                        ErrorCode: null,
                        ErrorMessage: null,
                        request.RootElement.Clone(),
                        response.RootElement.Clone())
                ]);
        }
    }
}
