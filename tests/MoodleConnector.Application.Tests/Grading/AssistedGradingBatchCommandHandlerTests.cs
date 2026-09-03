using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class AssistedGradingBatchCommandHandlerTests
{
    [Fact]
    public async Task CreateBatch_CriaLoteComItensAguardandoCorrecao()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            new FakeCourseContentsGateway(),
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway());

        var result = await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: [],
                MaxItems: 25,
                OnlyAwaitingGrading: true,
                IncludeRubric: false,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: true,
                TeacherInstructions: "Priorize clareza.",
                Priority: "high"),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.BatchJobId);
        Assert.Equal("10", result.CourseId);
        Assert.Equal(["501"], result.AssignmentIds);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.AcceptedItems);
        Assert.Equal(0, result.BlockedItems);
        Assert.Single(repository.Batches);
        Assert.Equal("Priorize clareza.", repository.Batches.Single().TeacherInstructions);
        Assert.Equal("high", repository.Batches.Single().Priority);
        Assert.False(repository.Batches.Single().IncludeRubric);
        Assert.False(repository.Batches.Single().IncludeSubmissionFiles);
        Assert.True(repository.Batches.Single().IncludeCourseMaterials);
        Assert.Equal(2, repository.Items.Count);
        Assert.All(repository.Items, item => Assert.Equal(GradingItemStatus.Pending, item.Status));
        Assert.Null(mediator.LastListQuery);
        Assert.Equal(result.BatchJobId, orchestrator.LastEnqueuedBatchId);
    }

    [Fact]
    public async Task CreateBatch_NaoRepeteFluxoCompostoQuandoAListagemFalha()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var submissionsGateway = new FakeAssignmentSubmissionsGateway { ThrowOnRead = true };
        var orchestrator = new FakeGradingBatchOrchestrator();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            new FakeCourseContentsGateway(),
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            submissionsGateway);

        await Assert.ThrowsAsync<MoodleApiException>(() => sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: [],
                MaxItems: 1,
                OnlyAwaitingGrading: true,
                IncludeRubric: false,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: false),
            CancellationToken.None));

        Assert.Equal(1, submissionsGateway.CallCount);
        Assert.Empty(repository.Batches);
    }

    [Fact]
    public async Task CreateBatch_AceitaIdDoModuloEResolveParaInstanciaDaTarefa()
    {
        var repository = new FakeGradingReviewRepository();
        var submissions = new FakeAssignmentSubmissionsGateway
        {
            NotFoundAssignmentIds = new HashSet<string>(["42"], StringComparer.Ordinal)
        };
        var contents = new FakeCourseContentsGateway();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            new FakeMediator(),
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            new FakeGradingBatchOrchestrator(),
            contents,
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            submissions);

        var result = await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                "321", "10", ["42"], [], 1, OnlyAwaitingGrading: true,
                IncludeRubric: false, IncludeSubmissionFiles: false),
            CancellationToken.None);

        Assert.Equal(["501"], result.AssignmentIds);
        Assert.Equal(1, contents.CallCount);
        Assert.Equal(2, submissions.CallCount);
    }

    [Fact]
    public async Task CreateBatch_NaoConverteFalhaDeDescobertaEmNoPendingSubmissions()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var sut = CreateHandler(repository, mediator, new FakeAssignmentSubmissionsGateway
        {
            FailAssignmentIds = new HashSet<string>(["501"], StringComparer.Ordinal)
        });

        await Assert.ThrowsAsync<MoodleApiException>(() => sut.Handle(
            new CreateAssistedGradingBatchCommand(
                "321", "10", ["501"], [], 25, OnlyAwaitingGrading: true),
            CancellationToken.None));

        Assert.Empty(repository.Batches);
    }

    [Fact]
    public async Task CreateBatch_RetornaPartialFailureQuandoOutraAtividadeFalha()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var sut = CreateHandler(repository, mediator, new FakeAssignmentSubmissionsGateway
        {
            FailAssignmentIds = new HashSet<string>(["502"], StringComparer.Ordinal)
        });

        var result = await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                "321", "10", ["501", "502"], [], 25, OnlyAwaitingGrading: true),
            CancellationToken.None);

        Assert.Equal("PartialFailure", result.Status);
        var failure = Assert.Single(result.DiscoveryFailures!);
        Assert.Equal("502", failure.AssignmentId);
        Assert.Equal(MoodleErrorContract.NetworkError, failure.ErrorCode);
        Assert.NotEqual(Guid.Empty, result.BatchJobId);
    }

    [Fact]
    public async Task CreateBatch_ReutilizaLoteQuandoRequestIdERepetido()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var sut = CreateHandler(repository, mediator);
        var request = new CreateAssistedGradingBatchCommand(
            "321", "10", ["501"], [], 25, OnlyAwaitingGrading: true,
            IdempotencyKey: "mcp-request-504-1");

        var first = await sut.Handle(request, CancellationToken.None);
        var replay = await sut.Handle(request, CancellationToken.None);

        Assert.Equal(first.BatchJobId, replay.BatchJobId);
        Assert.Equal("IdempotentReplay", replay.Status);
        Assert.Single(repository.Batches);
        Assert.Equal(0, mediator.ListQueryCallCount);
    }

    [Fact]
    public async Task CreateBatch_PreservaConexaoResolvidaParaWorkerAssincrono()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var credentialsProvider = new FakeCredentialsProvider(
            new MoodleConnectorCredentials(
                "client-fieg",
                "connection-fieg",
                "fieg",
                "https://ead.fieg.com.br",
                "user",
                "password",
                "fieg",
                CanWrite: true));
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            new FakeCourseContentsGateway(),
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway(),
            credentialsProvider: credentialsProvider);

        await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: [],
                MaxItems: 1,
                OnlyAwaitingGrading: true),
            CancellationToken.None);

        var batch = Assert.Single(repository.Batches);
        Assert.Equal("client-fieg", batch.ConnectorClientId);
        Assert.Equal("fieg", batch.ConnectionAlias);
        Assert.Equal(1, credentialsProvider.CallCount);
    }

    [Fact]
    public async Task CreateBatch_ComArquivosDeSubmissao_BaixaExtraiEPersisteArtefato()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var fileGateway = new FakeSubmissionFileGateway();
        var extraction = new FakeDocumentExtractionService();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            new FakeCourseContentsGateway(),
            fileGateway,
            extraction,
            new FakeAssignmentSubmissionsGateway());

        var result = await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: ["9001"],
                MaxItems: 25,
                OnlyAwaitingGrading: true,
                IncludeSubmissionFiles: true),
            CancellationToken.None);

        var artifact = Assert.Single(repository.Artifacts, artifact => artifact.ArtifactType == "submission_file");
        Assert.Equal(repository.Items.Single().Id, artifact.GradingItemId);
        Assert.Equal("submission_file", artifact.ArtifactType);
        Assert.Equal("entrega.txt", artifact.Filename);
        Assert.Equal("Texto extraido real da submissao.", artifact.ExtractedTextRef);
        Assert.Equal("succeeded", artifact.ExtractionStatus);
        Assert.Contains("https://moodle.example/pluginfile.php/entrega.txt", fileGateway.DownloadedFileUrls);
        Assert.Contains("entrega.txt", extraction.Filenames);
        Assert.Equal(result.BatchJobId, orchestrator.LastEnqueuedBatchId);
    }

    [Fact]
    public async Task CreateBatch_ComIngestaoDiferida_NaoBaixaExtraiNemConsultaConteudoPesado()
    {
        var repository = new FakeGradingReviewRepository();
        var fileGateway = new FakeSubmissionFileGateway();
        var extraction = new FakeDocumentExtractionService();
        var contentsGateway = new FakeCourseContentsGateway();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            new FakeMediator(),
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            new FakeGradingBatchOrchestrator(),
            contentsGateway,
            fileGateway,
            extraction,
            new FakeAssignmentSubmissionsGateway(),
            Options.Create(new GradingLimitsOptions { DeferHeavyIngestion = true }));

        await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: ["9001"],
                MaxItems: 25,
                OnlyAwaitingGrading: true,
                IncludeRubric: true,
                IncludeSubmissionFiles: true,
                IncludeCourseMaterials: true,
                PrefetchedSubmissions:
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
                        SubmittedAt: null,
                        ModifiedAt: null,
                        AttemptNumber: 0,
                        FileCount: 1,
                        HasOnlineText: false,
                        Files:
                        [
                            new AssignmentSubmissionFile(
                                "entrega.txt",
                                "text/plain",
                                31,
                                "https://moodle.example/pluginfile.php/entrega.txt?token=nao-persistir")
                        ])
                ]),
            CancellationToken.None);

        var artifact = Assert.Single(repository.Artifacts, item => item.ArtifactType == "submission_file");
        Assert.Equal(ExtractionStatus.Pending, artifact.ExtractionStatus);
        Assert.Equal("https://moodle.example/pluginfile.php/entrega.txt", artifact.SourceUrl);
        Assert.Equal("pending_ingestion", artifact.SummaryRef);
        Assert.Empty(fileGateway.DownloadedFileUrls);
        Assert.Empty(extraction.Filenames);
        Assert.Equal(0, contentsGateway.CallCount);
    }

    [Fact]
    public async Task IngestionService_MaterializaReferenciaPendenteEAtualizaArtifact()
    {
        var repository = new FakeGradingReviewRepository();
        var fileGateway = new FakeSubmissionFileGateway();
        var extraction = new FakeDocumentExtractionService();
        var batch = AssistedGradingBatch.Create(
            10,
            [501],
            "teacher-1",
            321,
            totalItems: 1,
            includeRubric: false,
            includeSubmissionFiles: true,
            includeCourseMaterials: false);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.AddArtifactAsync(
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "submission_file",
                "entrega.txt",
                "text/plain",
                Sha256: null,
                SizeBytes: 31,
                ExtractionStatus.Pending,
                ExtractedTextRef: null,
                SummaryRef: "pending_ingestion",
                DateTimeOffset.UtcNow,
                "https://moodle.example/pluginfile.php/entrega.txt"),
            CancellationToken.None);

        var sut = new GradingArtifactIngestionService(
            repository,
            new FakeAssignmentSubmissionsGateway(),
            new FakeCourseContentsGateway(),
            fileGateway,
            extraction,
            Options.Create(new GradingLimitsOptions()),
            NullLogger<GradingArtifactIngestionService>.Instance);

        await sut.IngestPendingAsync(batch, item, CancellationToken.None);

        var artifact = Assert.Single(repository.Artifacts);
        Assert.Equal(ExtractionStatus.Succeeded, artifact.ExtractionStatus);
        Assert.Equal("Texto extraido real da submissao.", artifact.ExtractedTextRef);
        Assert.Null(artifact.SourceUrl);
        Assert.Contains("https://moodle.example/pluginfile.php/entrega.txt", fileGateway.DownloadedFileUrls);
        Assert.Contains("entrega.txt", extraction.Filenames);
    }

    [Fact]
    public async Task IngestionService_ComMcpAtivo_AdiaExtracaoAteFallbackExplicito()
    {
        var repository = new FakeGradingReviewRepository();
        var fileGateway = new FakeSubmissionFileGateway();
        var extraction = new FakeDocumentExtractionService();
        var batch = AssistedGradingBatch.Create(
            10,
            [501],
            "teacher-1",
            321,
            totalItems: 1,
            includeRubric: false,
            includeSubmissionFiles: true,
            includeCourseMaterials: false);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.AddArtifactAsync(
            new GradingArtifact(
                Guid.NewGuid(), item.Id, "submission_file", "entrega.txt", "text/plain",
                Sha256: null, SizeBytes: 31, ExtractionStatus.Pending, ExtractedTextRef: null,
                SummaryRef: "pending_ingestion", DateTimeOffset.UtcNow,
                "https://moodle.example/pluginfile.php/entrega.txt"),
            CancellationToken.None);

        var sut = new GradingArtifactIngestionService(
            repository,
            new FakeAssignmentSubmissionsGateway(),
            new FakeCourseContentsGateway(),
            fileGateway,
            extraction,
            Options.Create(new GradingLimitsOptions()),
            NullLogger<GradingArtifactIngestionService>.Instance,
            resourceFeatures: Options.Create(new MoodleUniversalApiFeatureOptions
            {
                McpResourceSubmissionDeliveryEnabled = true
            }));

        await sut.IngestPendingAsync(batch, item, CancellationToken.None);

        var deferred = Assert.Single(repository.Artifacts);
        Assert.Equal(ExtractionStatus.Pending, deferred.ExtractionStatus);
        Assert.Null(deferred.ExtractedTextRef);
        Assert.NotNull(deferred.SourceUrl);
        Assert.Empty(fileGateway.DownloadedFileUrls);
        Assert.Empty(extraction.Filenames);

        await sut.MaterializeLegacySubmissionFallbackAsync(batch, item, CancellationToken.None);

        var materialized = Assert.Single(repository.Artifacts);
        Assert.Equal(ExtractionStatus.Succeeded, materialized.ExtractionStatus);
        Assert.Equal("Texto extraido real da submissao.", materialized.ExtractedTextRef);
        Assert.NotEmpty(fileGateway.DownloadedFileUrls);
    }

    [Fact]
    public async Task IngestionService_RetentaLeituraDeContextoTransitóriaAntesDeCriarArtifacts()
    {
        var repository = new FakeGradingReviewRepository();
        var contentsGateway = new FakeCourseContentsGateway { FailuresBeforeSuccess = 2 };
        var batch = AssistedGradingBatch.Create(
            10,
            [501],
            "teacher-1",
            321,
            totalItems: 1,
            includeRubric: false,
            includeSubmissionFiles: false,
            includeCourseMaterials: true);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);

        var sut = new GradingArtifactIngestionService(
            repository,
            new FakeAssignmentSubmissionsGateway(),
            contentsGateway,
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            Options.Create(new GradingLimitsOptions()),
            NullLogger<GradingArtifactIngestionService>.Instance);

        await sut.IngestPendingAsync(batch, item, CancellationToken.None);

        Assert.Equal(3, contentsGateway.CallCount);
        Assert.Contains(repository.Artifacts, artifact => artifact.ArtifactType == "assignment_context");
        Assert.DoesNotContain(repository.Artifacts, artifact => artifact.SummaryRef == "context_fetch_failed");
    }

    [Fact]
    public async Task IngestionService_PersisteDiagnosticoQuandoContextoNaoPodeSerLido()
    {
        var repository = new FakeGradingReviewRepository();
        var contentsGateway = new FakeCourseContentsGateway { FailuresBeforeSuccess = 3 };
        var batch = AssistedGradingBatch.Create(
            10,
            [501],
            "teacher-1",
            321,
            totalItems: 1,
            includeRubric: false,
            includeSubmissionFiles: false,
            includeCourseMaterials: true);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);

        var sut = new GradingArtifactIngestionService(
            repository,
            new FakeAssignmentSubmissionsGateway(),
            contentsGateway,
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            Options.Create(new GradingLimitsOptions()),
            NullLogger<GradingArtifactIngestionService>.Instance);

        await sut.IngestPendingAsync(batch, item, CancellationToken.None);

        var diagnostic = Assert.Single(repository.Artifacts, artifact => artifact.ArtifactType == "assignment_context");
        Assert.Equal("context_fetch_failed", diagnostic.SummaryRef);
        Assert.Equal(ExtractionStatus.Failed, diagnostic.ExtractionStatus);
    }

    [Fact]
    public async Task CreateBatch_ComMateriaisDoCurso_SalvaArtefatoDeContextoDaTarefa()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var fileGateway = new FakeSubmissionFileGateway();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            new FakeCourseContentsGateway(),
            fileGateway,
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway());

        await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: ["9001"],
                MaxItems: 25,
                OnlyAwaitingGrading: true,
                IncludeRubric: true,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: true),
            CancellationToken.None);

        var contextArtifacts = repository.Artifacts
            .Where(artifact => artifact.ArtifactType == "assignment_context")
            .ToArray();
        Assert.Contains(contextArtifacts, artifact =>
            artifact.Filename == "Orientacoes SAP 01 - Etapa 1.pdf" &&
            artifact.ExtractedTextRef == "Texto extraido real da submissao.");
        Assert.Contains(contextArtifacts, artifact =>
            artifact.Filename == "Tarefa 1" &&
            artifact.ExtractedTextRef!.Contains("Descricao da tarefa SAP 01", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fileGateway.DownloadedFileUrls, url => url == "https://moodle.example/pluginfile.php/orientacoes.pdf");
    }

    [Fact]
    public async Task CreateBatch_QuandoContextoFalha_PersisteDiagnosticoParaWorkerTentarNovamente()
    {
        var repository = new FakeGradingReviewRepository();
        var contentsGateway = new FakeCourseContentsGateway { FailuresBeforeSuccess = 3 };
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            new FakeMediator(),
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            new FakeGradingBatchOrchestrator(),
            contentsGateway,
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway());

        await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: ["9001"],
                MaxItems: 1,
                OnlyAwaitingGrading: true,
                IncludeRubric: false,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.Equal(3, contentsGateway.CallCount);
        var diagnostic = Assert.Single(repository.Artifacts, artifact => artifact.ArtifactType == "assignment_context");
        Assert.Equal(ExtractionStatus.Failed, diagnostic.ExtractionStatus);
        Assert.Equal("context_fetch_failed", diagnostic.SummaryRef);
    }

    [Fact]
    public async Task CreateBatch_SemRubrica_ComMateriais_NaoPersisteDescricaoDaAtividadeComoRubrica()
    {
        var repository = new FakeGradingReviewRepository();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            new FakeMediator(),
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            new FakeGradingBatchOrchestrator(),
            new FakeCourseContentsGateway(),
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway());

        await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                "321", "10", ["501"], ["9001"], 25, true,
                IncludeRubric: false,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.DoesNotContain(repository.Artifacts, artifact => artifact.SummaryRef == "assignment_description");
        Assert.Contains(repository.Artifacts, artifact => artifact.Filename == "Orientacoes SAP 01 - Etapa 1.pdf");
    }

    [Fact]
    public async Task CreateBatch_SemMateriaisDoCurso_NaoBaixaModulosVizinhos()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var fileGateway = new FakeSubmissionFileGateway();
        var contentsGateway = new FakeCourseContentsGateway();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            contentsGateway,
            fileGateway,
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway());

        await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: ["9001"],
                MaxItems: 25,
                OnlyAwaitingGrading: true,
                IncludeRubric: true,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: false),
            CancellationToken.None);

        Assert.Equal(1, contentsGateway.CallCount);
        Assert.Empty(fileGateway.DownloadedFileUrls);
        Assert.Contains(repository.Artifacts, artifact => artifact.Filename == "Tarefa 1");
        Assert.DoesNotContain(repository.Artifacts, artifact => artifact.Filename == "Orientacoes SAP 01 - Etapa 1.pdf");
    }

    [Fact]
    public async Task CreateBatch_ComDoisItensDaMesmaTarefa_ReusaCacheDeContexto()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var fileGateway = new FakeSubmissionFileGateway();
        var contentsGateway = new FakeCourseContentsGateway();
        var extraction = new FakeDocumentExtractionService();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            contentsGateway,
            fileGateway,
            extraction,
            new FakeAssignmentSubmissionsGateway());

        await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: [],
                MaxItems: 25,
                OnlyAwaitingGrading: true,
                IncludeRubric: true,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.Equal(2, repository.Items.Count);
        Assert.Equal(1, contentsGateway.CallCount);
        Assert.Single(fileGateway.DownloadedFileUrls, url => url == "https://moodle.example/pluginfile.php/orientacoes.pdf");
        Assert.Single(extraction.Filenames, filename => filename == "Orientacoes SAP 01 - Etapa 1.pdf");

        var contextArtifacts = repository.Artifacts
            .Where(artifact => artifact.ArtifactType == "assignment_context")
            .ToArray();
        Assert.Equal(6, contextArtifacts.Length);
        foreach (var item in repository.Items)
        {
            Assert.Contains(contextArtifacts, artifact =>
                artifact.GradingItemId == item.Id &&
                artifact.Filename == "Tarefa 1");
            Assert.Contains(contextArtifacts, artifact =>
                artifact.GradingItemId == item.Id &&
                artifact.Filename == "Orientacoes SAP 01 - Etapa 1.pdf");
        }
    }

    [Fact]
    public async Task CreateBatch_RespeitaMaxItemsESubmissionIds()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            new FakeCourseContentsGateway(),
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway());

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
        Assert.Null(mediator.LastListQuery);
        Assert.Equal(result.BatchJobId, orchestrator.LastEnqueuedBatchId);
    }

    [Fact]
    public async Task CreateBatch_SelecionaSubmissoesSemRefazerPaginacaoDoAgregador()
    {
        var repository = new FakeGradingReviewRepository();
        var mediator = new FakeMediator();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var sut = new CreateAssistedGradingBatchCommandHandler(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            orchestrator,
            new FakeCourseContentsGateway(),
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            new FakeAssignmentSubmissionsGateway());

        var result = await sut.Handle(
            new CreateAssistedGradingBatchCommand(
                UserExternalId: "321",
                CourseId: "10",
                AssignmentIds: ["501"],
                SubmissionIds: ["9001", "9002"],
                MaxItems: 2,
                OnlyAwaitingGrading: true),
            CancellationToken.None);

        Assert.Equal(2, result.AcceptedItems);
        Assert.Equal([9001L, 9002L], repository.Items.Select(item => item.SubmissionId).ToArray());
        Assert.Equal(0, mediator.ListQueryCallCount);
    }

    [Fact]
    public async Task CancelBatch_ChamaOrquestradorERetornaStatusCancelado()
    {
        var repository = new FakeGradingReviewRepository();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        orchestrator.BatchLookup = id => repository.Batches.SingleOrDefault(candidate => candidate.Id == id);
        var sut = new CancelAssistedGradingBatchCommandHandler(
            orchestrator,
            repository,
            new FakeCurrentUserContext("teacher-1"));

        var result = await sut.Handle(
            new CancelAssistedGradingBatchCommand(batch.Id),
            CancellationToken.None);

        Assert.Equal(batch.Id, orchestrator.LastCancelledBatchId);
        Assert.Equal(batch.Id, result.BatchJobId);
        Assert.Equal("Cancelled", result.Status);
        Assert.Contains("cancelado", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelBatch_DeOutroCriadorSemEscopoAdmin_DeveFalhar()
    {
        var repository = new FakeGradingReviewRepository();
        var orchestrator = new FakeGradingBatchOrchestrator();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = new CancelAssistedGradingBatchCommandHandler(
            orchestrator,
            repository,
            new FakeCurrentUserContext("teacher-2"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.Handle(new CancelAssistedGradingBatchCommand(batch.Id), CancellationToken.None));

        Assert.Null(orchestrator.LastCancelledBatchId);
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
        var sut = new GetAssistedGradingBatchStatusQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"));

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
        Assert.Equal(0, result.ProcessingMetrics.ProgressPercent);
        Assert.False(result.ProcessingMetrics.CanLaunch);
        Assert.Single(result.NextReadyItems);
        Assert.Empty(result.ErrorsByCategory);
    }

    [Fact]
    public async Task GetCoordinationReport_ConsolidaLoteInteiroParaCoordenacao()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501, 502], "teacher-1", 321, totalItems: 3);
        var lowConfidenceItem = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        var reviewedItem = AssistedGradingItem.Create(batch.Id, 10, 501, 9002, 102, 0);
        var failedItem = AssistedGradingItem.Create(batch.Id, 10, 502, 9003, 103, 0);
        lowConfidenceItem.SetDraft(4m, 0.35m, "Rascunho de baixa confianca.", "Revisar criterios.");
        reviewedItem.SetDraft(9m, 0.8m, "Rascunho consistente.");
        reviewedItem.ApplyTeacherReview(9.5m, "Feedback final revisado.", "teacher-1", 321, "approved");
        failedItem.MarkAnalysisFailed("Falha simulada ao processar contexto.");
        batch.UpdateCounters(processedItems: 3, readyItems: 2, blockedItems: 0, failedItems: 1);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(lowConfidenceItem, CancellationToken.None);
        await repository.AddItemAsync(reviewedItem, CancellationToken.None);
        await repository.AddItemAsync(failedItem, CancellationToken.None);
        await repository.AddEvidenceAsync(
            new GradingEvidence(
                Guid.NewGuid(),
                lowConfidenceItem.Id,
                "c1",
                "Descrever eventos de TI.",
                4m,
                2m,
                "Evidencia parcial.",
                "Faltou exemplo operacional.",
                TeacherReviewRequired: true,
                CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);
        await repository.AddEvidenceAsync(
            new GradingEvidence(
                Guid.NewGuid(),
                reviewedItem.Id,
                "c1",
                "Descrever eventos de TI.",
                4m,
                4m,
                "Evidencia suficiente.",
                GapsText: null,
                TeacherReviewRequired: false,
                CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);
        var sut = new GetAssistedGradingCoordinationReportQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"));

        var result = await sut.Handle(
            new GetAssistedGradingCoordinationReportQuery(batch.Id),
            CancellationToken.None);

        Assert.Equal(batch.Id, result.BatchJobId);
        Assert.Equal("10", result.CourseId);
        Assert.Equal(["501", "502"], result.AssignmentIds);
        Assert.Equal("ReadyForReview", result.Status);
        Assert.Equal(3, result.TotalItems);
        Assert.Equal(1, result.ReviewedItems);
        Assert.Equal(1, result.PendingReviewItems);
        Assert.Equal(1, result.LaunchPendingItems);
        Assert.Equal(1, result.LowConfidenceItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Equal(0.38m, result.AverageConfidence);
        Assert.Equal(6.5m, result.AverageSuggestedGrade);
        Assert.Equal(9.5m, result.AverageFinalGrade);
        Assert.Equal(1, result.StatusCounts["Failed"]);
        Assert.Equal(1, result.ReviewStatusCounts["Reviewed"]);
        Assert.Equal(1, result.CommitStatusCounts["Pending"]);
        Assert.Contains(result.AttentionItems, item =>
            item.GradingItemId == lowConfidenceItem.Id &&
            item.Reason.Contains("Baixa confianca", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.AttentionItems, item =>
            item.GradingItemId == failedItem.Id &&
            item.Reason.Contains("Falha no processamento", StringComparison.OrdinalIgnoreCase));
        var criterion = Assert.Single(result.CriteriaNeedingReview);
        Assert.Equal("c1", criterion.CriterionId);
        Assert.Equal(2, criterion.ItemCount);
        Assert.Equal(1, criterion.TeacherReviewRequiredItems);
        Assert.Equal(1, criterion.ItemsWithGaps);
        Assert.Contains("Relatorio consolidado", result.ReportMarkdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Itens que exigem atencao", result.ReportMarkdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetItem_RetornaDetalheMinimoDaCorrecao()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.", "Nota privada para o professor.");
        item.ApplyTeacherReview(8.5m, "Feedback final revisado.", "teacher-1", 321);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        var sut = new GetAssistedGradingItemQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"));

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
        Assert.Equal("Nota privada para o professor.", result.PrivateNotesToTeacher);
        Assert.Equal("Feedback final revisado.", result.FinalFeedback);
        Assert.Equal("Reviewed", result.ReviewStatus);
        Assert.False(string.IsNullOrWhiteSpace(result.DraftVersionHash));
        Assert.Empty(result.PendingIssues);
    }

    [Fact]
    public async Task GetItem_ComBaixaConfianca_RetornaPendenciaEObservacaoPrivada()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(4m, 0.35m, "Rascunho de baixa confianca.", "Baixa confianca: texto curto.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new GetAssistedGradingItemQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"));

        var result = await sut.Handle(
            new GetAssistedGradingItemQuery(item.Id, batch.Id),
            CancellationToken.None);

        Assert.Equal("Baixa confianca: texto curto.", result.PrivateNotesToTeacher);
        Assert.Contains(result.PendingIssues, issue => issue.Contains("Baixa confianca", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetItem_ComFalhaDeAnalise_RetornaPendenciaDeProcessamento()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.MarkAnalysisFailed("Falha simulada ao processar contexto.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new GetAssistedGradingItemQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"));

        var result = await sut.Handle(
            new GetAssistedGradingItemQuery(item.Id, batch.Id),
            CancellationToken.None);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("Falha simulada ao processar contexto.", result.PrivateNotesToTeacher);
        Assert.Contains(result.PendingIssues, issue => issue.Contains("Falha no processamento", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetItem_RetornaEvidenciasPorCriterio()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.AddEvidenceAsync(
            new GradingEvidence(
                Guid.NewGuid(),
                item.Id,
                "c1",
                "Descrever eventos de TI.",
                4m,
                3m,
                "O texto descreve monitoramento e alerta.",
                "Faltou exemplo operacional.",
                TeacherReviewRequired: true,
                CreatedAt: new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        var sut = new GetAssistedGradingItemQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"));

        var result = await sut.Handle(
            new GetAssistedGradingItemQuery(item.Id, batch.Id),
            CancellationToken.None);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal("c1", evidence.CriterionId);
        Assert.Equal("Descrever eventos de TI.", evidence.CriterionText);
        Assert.Equal(4m, evidence.MaxPoints);
        Assert.Equal(3m, evidence.SuggestedPoints);
        Assert.Equal("O texto descreve monitoramento e alerta.", evidence.EvidenceText);
        Assert.Equal("Faltou exemplo operacional.", evidence.GapsText);
        Assert.True(evidence.TeacherReviewRequired);
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
        var sut = new GetAssistedGradingItemQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"));

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
            new FakeAuditLogRepository(),
            new FakeMoodleAssignmentSettingsGateway());

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
        Assert.False(string.IsNullOrWhiteSpace(result.DraftVersionHash));
        Assert.Empty(result.PendingIssues);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateDraft_PermiteFeedbackSemNota()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(null, 0.8m, "Rascunho de feedback.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new UpdateAssistedGradingDraftCommandHandler(
            repository,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            new FakeMoodleAssignmentSettingsGateway());

        var result = await sut.Handle(
            new UpdateAssistedGradingDraftCommand(
                GradingItemId: item.Id,
                FinalGrade: null,
                FinalFeedback: "Feedback final sem nota.",
                TeacherDecision: "approved",
                ReviewNotes: null,
                ExpectedReviewStatus: "NotReviewed"),
            CancellationToken.None);

        Assert.Null(result.FinalGrade);
        Assert.Equal("Feedback final sem nota.", result.FinalFeedback);
        Assert.Equal("Reviewed", result.ReviewStatus);
        Assert.Equal("Pending", result.CommitStatus);
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
            new FakeAuditLogRepository(),
            new FakeMoodleAssignmentSettingsGateway());

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
            new FakeAuditLogRepository(),
            new FakeMoodleAssignmentSettingsGateway());

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

    [Fact]
    public async Task GetBatchStatus_DeOutroCriadorSemEscopoAdmin_DeveFalhar()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = new GetAssistedGradingBatchStatusQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-2"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.Handle(new GetAssistedGradingBatchStatusQuery(batch.Id, Page: 1, PageSize: 10), CancellationToken.None));

        Assert.Equal("Usuario atual nao esta autorizado a acessar este lote de correcao.", ex.Message);
    }

    [Fact]
    public async Task GetCoordinationReport_DeOutroCriadorSemEscopoAdmin_DeveFalhar()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = new GetAssistedGradingCoordinationReportQueryHandler(
            repository,
            new FakeCurrentUserContext("teacher-2"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.Handle(new GetAssistedGradingCoordinationReportQuery(batch.Id), CancellationToken.None));

        Assert.Equal("Usuario atual nao esta autorizado a acessar este lote de correcao.", ex.Message);
    }

    [Fact]
    public async Task GetItem_DeOutroCriadorComEscopoAdmin_DevePermitir()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new GetAssistedGradingItemQueryHandler(
            repository,
            new FakeCurrentUserContext("coordinator-1", ["grading.admin"]));

        var result = await sut.Handle(new GetAssistedGradingItemQuery(item.Id, batch.Id), CancellationToken.None);

        Assert.Equal(item.Id, result.GradingItemId);
    }

    [Fact]
    public async Task UpdateDraft_DeOutroCriadorSemEscopoAdmin_DeveFalhar()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = new UpdateAssistedGradingDraftCommandHandler(
            repository,
            new FakeCurrentUserContext("teacher-2"),
            new FakeMoodleUserResolver(654),
            new FakeAuditLogRepository(),
            new FakeMoodleAssignmentSettingsGateway());

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.Handle(
                new UpdateAssistedGradingDraftCommand(
                    item.Id,
                    FinalGrade: 8.5m,
                    FinalFeedback: "Feedback final revisado.",
                    TeacherDecision: "approved",
                    ReviewNotes: null,
                    ExpectedReviewStatus: "NotReviewed"),
                CancellationToken.None));

        Assert.Equal("Usuario atual nao esta autorizado a acessar este lote de correcao.", ex.Message);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    private static CreateAssistedGradingBatchCommandHandler CreateHandler(
        FakeGradingReviewRepository repository,
        FakeMediator mediator,
        FakeAssignmentSubmissionsGateway? submissionsGateway = null) =>
        new(
            repository,
            mediator,
            new FakeCurrentUserContext("teacher-1"),
            new FakeMoodleUserResolver(321),
            new FakeAuditLogRepository(),
            new FakeGradingBatchOrchestrator(),
            new FakeCourseContentsGateway(),
            new FakeSubmissionFileGateway(),
            new FakeDocumentExtractionService(),
            submissionsGateway ?? new FakeAssignmentSubmissionsGateway());

    private sealed class FakeGradingReviewRepository : IGradingReviewRepository
    {
        public List<AssistedGradingBatch> Batches { get; } = [];

        public List<AssistedGradingItem> Items { get; } = [];

        public List<GradingArtifact> Artifacts { get; } = [];

        public List<GradingEvidence> Evidence { get; } = [];

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

        public Task<AssistedGradingBatch?> GetBatchByIdempotencyKeyAsync(
            string createdBySubject,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(Batches.SingleOrDefault(batch =>
                batch.CreatedBySubject == createdBySubject &&
                batch.IdempotencyKey == idempotencyKey));

        public Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken)
        {
            Artifacts.Add(artifact);
            return Task.CompletedTask;
        }

        public Task UpdateArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken)
        {
            var index = Artifacts.FindIndex(existing => existing.Id == artifact.Id);
            if (index < 0)
            {
                throw new InvalidOperationException("Artifact nao encontrado no fake.");
            }

            Artifacts[index] = artifact;
            return Task.CompletedTask;
        }

        public Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken)
        {
            Evidence.Add(evidence);
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
            return Task.FromResult<IReadOnlyList<GradingArtifact>>(Artifacts
                .Where(artifact => artifact.GradingItemId == gradingItemId)
                .ToArray());
        }

        public Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(
            Guid gradingItemId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GradingEvidence>>(Evidence
                .Where(evidence => evidence.GradingItemId == gradingItemId)
                .ToArray());
        }

        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
            GradingBatchStatus status, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUserContext(string subject, IReadOnlyCollection<string>? scopes = null) : ICurrentUserContext
    {
        public string Subject { get; } = subject;
        public string? Email => "teacher@example.com";
        public IReadOnlyCollection<string> Scopes { get; } = scopes ?? [];

        public bool HasScope(string scope)
        {
            return Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeMoodleAssignmentSettingsGateway : IMoodleAssignmentSettingsGateway
    {
        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            string assignmentId,
            CancellationToken cancellationToken)
            => Task.FromResult<AssignmentSettingsSummary?>(new AssignmentSettingsSummary(assignmentId, 10m));
    }

    private sealed class FakeMoodleUserResolver(long? moodleUserId) : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(moodleUserId);
        }
    }

    private sealed class FakeCredentialsProvider(MoodleConnectorCredentials credentials)
        : IMoodleConnectorCredentialsProvider
    {
        public int CallCount { get; private set; }

        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(credentials);
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

        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
            GradingBatchStatus status, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeGradingBatchOrchestrator : IGradingBatchOrchestrator
    {
        public Guid? LastEnqueuedBatchId { get; private set; }

        public Guid? LastCancelledBatchId { get; private set; }

        public Task EnqueueAsync(Guid batchId, CancellationToken cancellationToken)
        {
            LastEnqueuedBatchId = batchId;
            return Task.CompletedTask;
        }

        public Task CancelAsync(Guid batchId, CancellationToken cancellationToken)
        {
            LastCancelledBatchId = batchId;
            var batch = BatchLookup?.Invoke(batchId);
            batch?.Cancel();
            return Task.CompletedTask;
        }

        public Func<Guid, AssistedGradingBatch?>? BatchLookup { get; set; }

        public Task<GradingBatchOrchestratorStatus> GetStatusAsync(Guid batchId, CancellationToken cancellationToken)
        {
            var batch = BatchLookup?.Invoke(batchId)
                ?? AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 0);
            return Task.FromResult(new GradingBatchOrchestratorStatus(
                batchId,
                batch.Status,
                batch.TotalItems,
                batch.ProcessedItems,
                batch.ReadyItems,
                batch.BlockedItems,
                batch.FailedItems,
                IsQueued: false,
                LastError: null));
        }
    }

    private sealed class FakeMediator : IMediator
    {
        public ListAssignmentSubmissionsQuery? LastListQuery { get; private set; }

        public int ListQueryCallCount { get; private set; }

        public int FailuresBeforeSuccess { get; init; }

        public ISet<string> FailAssignmentIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        public bool UsePagedRows { get; init; }

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
                ListQueryCallCount++;
                if (FailAssignmentIds.Contains(list.AssignmentId) || ListQueryCallCount <= FailuresBeforeSuccess)
                {
                    throw new MoodleApiException(
                        MoodleErrorContract.NetworkError,
                        "falha de rede simulada");
                }
                return Task.FromResult((TResponse)(object)CreatePage(list, UsePagedRows));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is ListAssignmentSubmissionsQuery list)
            {
                LastListQuery = list;
                return Task.FromResult<object?>(CreatePage(list, UsePagedRows));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();

        private static AssignmentSubmissionsPage CreatePage(
            ListAssignmentSubmissionsQuery query,
            bool usePagedRows = false)
        {
            if (usePagedRows)
            {
                var allRows = Enumerable.Range(1, 8)
                    .Select(index => new AssignmentSubmissionSummary(
                        (100 + index).ToString(),
                        $"Aluno {index}",
                        (9000 + index).ToString(),
                        "submitted",
                        "notgraded",
                        Submitted: true,
                        Late: false,
                        NeedsGrading: true,
                        SubmittedAt: new DateTimeOffset(2026, 6, 10, index, 0, 0, TimeSpan.Zero),
                        ModifiedAt: new DateTimeOffset(2026, 6, 10, index, 0, 0, TimeSpan.Zero),
                        AttemptNumber: 0,
                        FileCount: 0,
                        HasOnlineText: true))
                    .ToArray();
                var pageRows = allRows
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize + 1)
                    .ToArray();

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
                    Total: allRows.Length,
                    HasMore: pageRows.Length > query.PageSize,
                    pageRows.Take(query.PageSize).ToArray());
            }

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
                        HasOnlineText: true,
                        Files:
                        [
                            new AssignmentSubmissionFile(
                                "entrega.txt",
                                "text/plain",
                                31,
                                "https://moodle.example/pluginfile.php/entrega.txt")
                        ]),
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

    private sealed class FakeSubmissionFileGateway : IMoodleSubmissionFileGateway
    {
        public string? LastFileUrl { get; private set; }

        public List<string> DownloadedFileUrls { get; } = [];

        public Task<SubmissionFileDownloadResult> DownloadFileAsync(
            string userExternalId,
            string fileUrl,
            string filename,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            LastFileUrl = fileUrl;
            DownloadedFileUrls.Add(fileUrl);
            return Task.FromResult(new SubmissionFileDownloadResult(
                filename,
                "text/plain",
                31,
                "sha-1",
                "Texto extraido real da submissao."u8.ToArray(),
                Truncated: false));
        }
    }

    private sealed class FakeCourseContentsGateway : IMoodleCourseContentsGateway
    {
        public int CallCount { get; private set; }

        public int FailuresBeforeSuccess { get; init; }

        public Task<CourseContentsSummary> GetCourseContentsAsync(
            string userExternalId,
            string courseId,
            IReadOnlyCollection<string> moduleTypes,
            bool includeHidden,
            bool onlyWithFiles,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount <= FailuresBeforeSuccess)
            {
                throw new MoodleApiException(
                    MoodleErrorContract.NetworkError,
                    "falha de rede simulada");
            }

            return Task.FromResult(new CourseContentsSummary(
                courseId,
                moduleTypes.ToArray(),
                includeHidden,
                onlyWithFiles,
                Sections:
                [
                    new CourseSectionSummary(
                        "1",
                        SectionNumber: 1,
                        "SAP 01",
                        Summary: null,
                        Visible: true,
                        ModuleCount: 3,
                        IsEmpty: false,
                        Modules:
                        [
                            new CourseModuleSummary(
                                "499",
                                InstanceId: "900",
                                "resource",
                                "Calendario do curso",
                                Url: null,
                                Visible: true,
                                UserVisible: true,
                                Description: "Datas gerais.",
                                AvailabilityInfo: null,
                                Dates: [],
                                Files: []),
                            new CourseModuleSummary(
                                "500",
                                InstanceId: "901",
                                "resource",
                                "Orientacoes SAP 01 - Etapa 1",
                                Url: null,
                                Visible: true,
                                UserVisible: true,
                                Description: null,
                                AvailabilityInfo: null,
                                Dates: [],
                                Files:
                                [
                                    new CourseModuleFile(
                                        Type: "file",
                                        FileName: "Orientacoes SAP 01 - Etapa 1.pdf",
                                        FilePath: "/",
                                        FileSize: 100,
                                        MimeType: "application/pdf",
                                        FileUrl: "https://moodle.example/pluginfile.php/orientacoes.pdf",
                                        IsExternalFile: false)
                                ]),
                            new CourseModuleSummary(
                                "42",
                                InstanceId: "501",
                                "assign",
                                "Tarefa 1",
                                Url: null,
                                Visible: true,
                                UserVisible: true,
                                Description: "Descricao da tarefa SAP 01 etapa 1.",
                                AvailabilityInfo: null,
                                Dates: [],
                                Files: [])
                        ])
                ]));
        }
    }

    private sealed class FakeDocumentExtractionService : IDocumentExtractionService
    {
        public string? LastFilename { get; private set; }

        public List<string> Filenames { get; } = [];

        public Task<DocumentExtractionResult> ExtractAsync(
            string filename,
            string mimeType,
            byte[] content,
            CancellationToken cancellationToken)
        {
            LastFilename = filename;
            Filenames.Add(filename);
            return Task.FromResult(new DocumentExtractionResult(
                filename,
                mimeType,
                ExtractionStatus.Succeeded,
                "Texto extraido real da submissao.",
                WordCount: 5,
                CharCount: 31,
                Truncated: false,
                ErrorMessage: null));
        }
    }

    private sealed class FakeAssignmentSubmissionsGateway : IMoodleAssignmentSubmissionsGateway
    {
        public int CallCount { get; private set; }

        public bool ThrowOnRead { get; init; }

        public ISet<string> FailAssignmentIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        public ISet<string> NotFoundAssignmentIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

        public Task<IReadOnlyList<AssignmentSubmissionsBatch>> GetAssignmentSubmissionsBatchAsync(
            string userExternalId,
            IReadOnlyCollection<string> assignmentIds,
            string? status,
            DateTimeOffset? since,
            DateTimeOffset? before,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowOnRead)
            {
                throw new MoodleApiException(MoodleErrorContract.NetworkError, "falha de rede simulada", functionName: "mod_assign_get_submissions");
            }

            return Task.FromResult<IReadOnlyList<AssignmentSubmissionsBatch>>(assignmentIds
                .Select(assignmentId => FailAssignmentIds.Contains(assignmentId)
                    ? new AssignmentSubmissionsBatch(assignmentId, [], MoodleErrorContract.NetworkError, "falha de rede simulada")
                    : NotFoundAssignmentIds.Contains(assignmentId)
                        ? new AssignmentSubmissionsBatch(assignmentId, [], "assignment_not_found", "tarefa nao encontrada")
                    : new AssignmentSubmissionsBatch(assignmentId, CreateSubmissions()))
                .ToArray());
        }

        public Task<IReadOnlyList<AssignmentSubmissionRecord>> GetAssignmentSubmissionsAsync(
            string userExternalId,
            string assignmentId,
            string? status,
            DateTimeOffset? since,
            DateTimeOffset? before,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AssignmentSubmissionRecord>>(CreateSubmissions());
        }

        private static IReadOnlyList<AssignmentSubmissionRecord> CreateSubmissions() =>
        [
            new AssignmentSubmissionRecord(
                "9001", "101", "submitted", "notgraded", null, null, 0, 1, true,
                [new AssignmentSubmissionFile("entrega.txt", "text/plain", 31, "https://moodle.example/pluginfile.php/entrega.txt")]),
            new AssignmentSubmissionRecord(
                "9002", "102", "submitted", "notgraded", null, null, 0, 0, true)
        ];
    }
}
