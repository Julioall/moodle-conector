using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain.Grading;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class GradingReviewRepositoryTests
{
    [Fact]
    public async Task GradingRun_AgrupaSublotesEPermanecePersistido()
    {
        await using var dbContext = CreateDbContext();
        IGradingReviewRepository repository = new GradingReviewRepository(dbContext);
        var run = GradingRun.Create("teacher-1", 321, "connection-1", "client-1", "alias-1", "10");
        var first = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, 1, gradingRunId: run.Id);
        var second = AssistedGradingBatch.Create(10, [502], "teacher-1", 321, 1, gradingRunId: run.Id);

        await repository.AddGradingRunAsync(run, CancellationToken.None);
        await repository.AddBatchAsync(first, CancellationToken.None);
        await repository.AddBatchAsync(second, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var loaded = await repository.GetGradingRunAsync(run.Id, CancellationToken.None);
        var children = await repository.ListBatchesByGradingRunAsync(run.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("connection-1", loaded!.MoodleConnectionId);
        Assert.Equal([first.Id, second.Id], children.Select(batch => batch.Id));
    }

    [Fact]
    public async Task PublicationClaims_MantemAlvosNaoConflitantesQuandoParteDoLoteEstaOcupada()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var connectionKey = "connection-claims";
        var occupied = new GradingPublicationClaimEntity
        {
            PublicationId = Guid.NewGuid(),
            GradingItemId = Guid.NewGuid(),
            ConnectionKey = connectionKey,
            AssignmentId = 501,
            MoodleUserId = 101,
            AttemptNumber = 1,
            Status = "Authorized",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        dbContext.GradingPublicationClaims.Add(occupied);
        await dbContext.SaveChangesAsync();

        var freeItemId = Guid.NewGuid();
        var results = await store.TryClaimPublicationTargetsAsync(
            Guid.NewGuid(),
            connectionKey,
            [
                new GradingPublicationClaimRequest(Guid.NewGuid(), 501, 101, 1),
                new GradingPublicationClaimRequest(freeItemId, 501, 202, 1)
            ],
            DateTimeOffset.UtcNow.AddMinutes(15),
            CancellationToken.None);

        Assert.False(results.Single(result => result.GradingItemId != freeItemId).Claimed);
        Assert.True(results.Single(result => result.GradingItemId == freeItemId).Claimed);
        Assert.Contains(dbContext.GradingPublicationClaims,
            claim => claim.ConnectionKey == connectionKey && claim.MoodleUserId == 202 && claim.Status == "AwaitingConfirmation");
    }

    [Fact]
    public async Task PublicationClaims_VinculoAoPendingActionProtegePreviewAutorizadaContraExpiracao()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var action = new MoodleConnector.Domain.PendingMoodleAction
        {
            ToolName = "criar_previa_lancamento_lote",
            CreatedBySubject = "teacher-1",
            PayloadJson = "{}",
            PreviewJson = "{}",
            ConfirmationText = "CONFIRMAR",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        var publicationId = Guid.NewGuid();
        dbContext.PendingMoodleActions.Add(action);
        dbContext.GradingPublicationClaims.Add(new GradingPublicationClaimEntity
        {
            PublicationId = publicationId,
            GradingItemId = Guid.NewGuid(),
            ConnectionKey = "connection-binding",
            AssignmentId = 501,
            MoodleUserId = 101,
            AttemptNumber = 1,
            Status = "AwaitingConfirmation",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await dbContext.SaveChangesAsync();

        await store.BindPublicationClaimsAsync(publicationId, action.Id, CancellationToken.None);
        action.Authorize("teacher-1", DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync();

        var result = await store.TryClaimPublicationTargetsAsync(
            Guid.NewGuid(),
            "connection-binding",
            [new GradingPublicationClaimRequest(Guid.NewGuid(), 501, 101, 1)],
            DateTimeOffset.UtcNow.AddMinutes(15),
            CancellationToken.None);

        Assert.False(Assert.Single(result).Claimed);
        Assert.Equal("publication_target_busy", result[0].ConflictCode);
        Assert.Equal("AwaitingConfirmation", Assert.Single(dbContext.GradingPublicationClaims).Status);
    }

    [Fact]
    public async Task PublicationClaims_MesmaPublicacaoPodeReivindicarNovamenteAposFalhaParcial()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var publicationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        dbContext.GradingPublicationClaims.Add(new GradingPublicationClaimEntity
        {
            PublicationId = publicationId,
            GradingItemId = itemId,
            ConnectionKey = "connection-retry",
            AssignmentId = 501,
            MoodleUserId = 101,
            AttemptNumber = 1,
            Status = "Authorized",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        });
        await dbContext.SaveChangesAsync();

        var result = await store.TryClaimPublicationTargetsAsync(
            publicationId,
            "connection-retry",
            [new GradingPublicationClaimRequest(itemId, 501, 101, 1)],
            DateTimeOffset.UtcNow.AddMinutes(15),
            CancellationToken.None);

        Assert.True(Assert.Single(result).Claimed);
        Assert.Single(dbContext.GradingPublicationClaims);
        Assert.Equal("Authorized", dbContext.GradingPublicationClaims.Single().Status);
    }

    [Fact]
    public async Task AddBatchAndItemAsync_PersisteCicloMinimoDeCorrecao()
    {
        await using var dbContext = CreateDbContext();
        IGradingReviewRepository repository = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(
            courseId: 10,
            assignmentIds: [501],
            createdBySubject: "teacher-1",
            createdByMoodleUserId: 321,
            totalItems: 1);
        var item = AssistedGradingItem.Create(
            batch.Id,
            courseId: 10,
            assignmentId: 501,
            submissionId: 9001,
            moodleUserId: 101,
            attemptNumber: 0);
        item.SetDraft(9m, 0.9m, "Rascunho.");
        item.ApplyTeacherReview(
            9.5m,
            "Feedback final.",
            "teacher-1",
            321,
            "approved",
            "Ajuste de nota validado.");

        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var savedBatch = await repository.GetBatchAsync(batch.Id, CancellationToken.None);
        var savedItem = await repository.GetItemAsync(item.Id, CancellationToken.None);

        Assert.NotNull(savedBatch);
        Assert.Equal([501], savedBatch!.AssignmentIds);
        Assert.NotNull(savedItem);
        Assert.Equal(GradingItemStatus.ReadyToCommit, savedItem!.Status);
        Assert.Equal(9.5m, savedItem.FinalGrade);
        Assert.Equal("approved", savedItem.TeacherDecision);
        Assert.Equal("Ajuste de nota validado.", savedItem.ReviewNotes);
        Assert.NotNull(savedItem.IdempotencyKey);
    }

    [Fact]
    public async Task AddEvidenceAsync_PersisteEListaEvidenciasDoItem()
    {
        await using var dbContext = CreateDbContext();
        IGradingReviewRepository repository = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(
            courseId: 10,
            assignmentIds: [501],
            createdBySubject: "teacher-1",
            createdByMoodleUserId: 321,
            totalItems: 1);
        var item = AssistedGradingItem.Create(
            batch.Id,
            courseId: 10,
            assignmentId: 501,
            submissionId: 9001,
            moodleUserId: 101,
            attemptNumber: 0);
        var evidence = new GradingEvidence(
            Guid.NewGuid(),
            item.Id,
            "c1",
            "Descrever eventos de TI.",
            4m,
            3m,
            "O texto menciona monitoramento e alerta.",
            "Faltou exemplo operacional.",
            TeacherReviewRequired: true,
            CreatedAt: new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero));

        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.AddEvidenceAsync(evidence, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var saved = Assert.Single(await repository.ListEvidenceByItemAsync(item.Id, CancellationToken.None));
        Assert.Equal("c1", saved.CriterionId);
        Assert.Equal("Descrever eventos de TI.", saved.CriterionText);
        Assert.Equal(4m, saved.MaxPoints);
        Assert.Equal(3m, saved.SuggestedPoints);
        Assert.Equal("O texto menciona monitoramento e alerta.", saved.EvidenceText);
        Assert.Equal("Faltou exemplo operacional.", saved.GapsText);
        Assert.True(saved.TeacherReviewRequired);
    }

    [Fact]
    public async Task ContextSnapshotStore_PublicaPayloadDeFormaIdempotente()
    {
        await using var dbContext = CreateDbContext();
        var repository = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var context = GradingContext.Build(
            item.Id,
            batch.Id,
            "10",
            "501",
            "9001",
            "101",
            assignmentStatement: "Enunciado da atividade.",
            criteria: "Descrever a solução.",
            rubricDescription: null,
            maxGrade: 10m,
            gradeScale: null,
            submissionText: "Texto que não deve ser duplicado no snapshot.",
            attachedFiles: [],
            courseMaterials: null,
            teacherInstructions: "Seja claro.");
        var snapshot = GradingContextSnapshotFactory.Create(
            item,
            context,
            new GradingContextOptions());

        IGradingContextSnapshotStore store = repository;
        await store.PublishAsync(snapshot, CancellationToken.None);
        await store.PublishAsync(snapshot, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var document = Assert.Single(dbContext.GradingContextSnapshots);
        Assert.Equal(snapshot.ContextHash, document.ContextHash);
        Assert.Equal(snapshot.Version, document.Version);
        Assert.Contains("AssignmentStatement", document.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Texto que não deve ser duplicado", document.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposalStore_PublicaVersoesAppendOnlyEDeFormaIdempotente()
    {
        await using var dbContext = CreateDbContext();
        var repository = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        dbContext.GradingBatches.Add(batch);
        dbContext.GradingItems.Add(item);
        await dbContext.SaveChangesAsync();

        var context = GradingContext.Build(
            item.Id,
            batch.Id,
            "10",
            "501",
            "9001",
            "101",
            "Enunciado",
            "Criterio",
            null,
            10m,
            null,
            "Texto de entrega",
            [],
            null,
            null);
        var snapshot = GradingContextSnapshotFactory.Create(item, context, new GradingContextOptions());
        item.RecordContextSnapshot(snapshot);
        var proposal = AiGradingProposal.Create(
            item.Id,
            batch.Id,
            await repository.GetNextVersionAsync(item.Id, CancellationToken.None),
            item.ContextHash,
            8m,
            "Feedback",
            [new AiGradingCriterionProposal("C1", "Criterio", 10m, 8m, AiGradingCriterionSource.FormalRubric, "evidencia", null, false, false, [])],
            [],
            [],
            new GradingScaleSnapshot(10m, "points", "Moodle"),
            new GradingExtractionSummary("succeeded", 1, false, 20, 20, null),
            new GradingEvidenceCoverage(1, 1, 1, 1, 20, 20, false),
            new AiGradingConfidenceResult(.9m, [], false),
            reviewRequired: false,
            createdAt: DateTimeOffset.UtcNow);

        IGradingProposalStore store = repository;
        await store.PublishAsync(proposal, CancellationToken.None);
        await store.PublishAsync(proposal, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);

        var persisted = Assert.Single(dbContext.AiGradingProposals);
        Assert.Equal(proposal.ProposalHash, persisted.ProposalHash);
        Assert.Contains("C1", persisted.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Texto de entrega", persisted.PayloadJson, StringComparison.Ordinal);
        Assert.Equal(2, await store.GetNextVersionAsync(item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RetentionStore_RedigeTextoExpiradoPreservaHashEEstado()
    {
        await using var dbContext = CreateDbContext();
        var repository = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        var expired = new GradingArtifact(
            Guid.NewGuid(), item.Id, "submission_file", "answer.txt", "text/plain", "abc123", 10,
            ExtractionStatus.Succeeded, "texto confidencial", null, DateTimeOffset.UtcNow.AddDays(-10));
        var recent = expired with { Id = Guid.NewGuid(), ExtractedTextRef = "texto recente", CreatedAt = DateTimeOffset.UtcNow };
        var context = expired with { Id = Guid.NewGuid(), ArtifactType = "assignment_context", ExtractedTextRef = "enunciado", CreatedAt = DateTimeOffset.UtcNow.AddDays(-10) };
        dbContext.GradingBatches.Add(batch);
        dbContext.GradingItems.Add(item);
        dbContext.GradingArtifacts.AddRange(expired, recent, context);
        await dbContext.SaveChangesAsync();

        IGradingRetentionStore store = repository;
        var redacted = await store.RedactExpiredArtifactTextAsync(
            DateTimeOffset.UtcNow.AddDays(-7),
            CancellationToken.None);

        Assert.Equal(1, redacted);
        var savedExpired = await dbContext.GradingArtifacts.SingleAsync(artifact => artifact.Id == expired.Id);
        Assert.Null(savedExpired.ExtractedTextRef);
        Assert.Equal("retention_redacted", savedExpired.SummaryRef);
        Assert.Equal(expired.Sha256, savedExpired.Sha256);
        Assert.Equal(expired.ExtractionStatus, savedExpired.ExtractionStatus);
        Assert.Equal("texto recente", (await dbContext.GradingArtifacts.SingleAsync(artifact => artifact.Id == recent.Id)).ExtractedTextRef);
        Assert.Equal("enunciado", (await dbContext.GradingArtifacts.SingleAsync(artifact => artifact.Id == context.Id)).ExtractedTextRef);
    }

    [Fact]
    public async Task JobStore_ImpedeClaimConcorrenteRenovaERegistraCheckpoint()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        dbContext.GradingBatches.Add(batch);
        dbContext.GradingItems.Add(item);
        await dbContext.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var first = await store.TryClaimBatchAsync(
            batch.Id, "worker-a", now, TimeSpan.FromMinutes(10), CancellationToken.None);
        var second = await store.TryClaimBatchAsync(
            batch.Id, "worker-b", now, TimeSpan.FromMinutes(10), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.True(await store.RenewBatchLeaseAsync(
            batch.Id, "worker-a", now.AddMinutes(1), TimeSpan.FromMinutes(10), CancellationToken.None));
        Assert.True(await store.UpdateBatchCheckpointAsync(
            batch.Id, "worker-a", item.Id, now.AddMinutes(1), CancellationToken.None));
        Assert.True(await store.ReleaseBatchLeaseAsync(
            batch.Id, "worker-a", now.AddMinutes(2), null, null, CancellationToken.None));

        var savedBatch = await store.GetBatchAsync(batch.Id, CancellationToken.None);
        Assert.NotNull(savedBatch);
        Assert.Null(savedBatch!.LeaseOwner);
        Assert.Null(savedBatch.LeaseUntil);
        Assert.Equal(item.Id, savedBatch.CheckpointItemId);
        Assert.Equal(1, savedBatch.AttemptCount);
    }

    [Fact]
    public async Task JobStore_RecuperaLeaseExpiradoSomenteQuandoHaItemPendente()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        dbContext.GradingBatches.Add(batch);
        dbContext.GradingItems.Add(item);
        await dbContext.SaveChangesAsync();

        var claimTime = DateTimeOffset.UtcNow;
        Assert.NotNull(await store.TryClaimBatchAsync(
            batch.Id, "worker-a", claimTime, TimeSpan.FromMinutes(1), CancellationToken.None));

        var recovered = await store.RecoverExpiredBatchLeasesAsync(
            claimTime.AddMinutes(2), CancellationToken.None);

        Assert.Equal(1, recovered);
        var savedBatch = await store.GetBatchAsync(batch.Id, CancellationToken.None);
        Assert.Equal(GradingBatchStatus.Pending, savedBatch!.Status);
        Assert.Null(savedBatch.LeaseOwner);
        Assert.Equal(claimTime.AddMinutes(2), savedBatch.NextAttemptAt);
    }

    [Fact]
    public async Task JobStore_ClaimDueRespeitaPrioridadeAntesDaOrdemDeCriacao()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var low = AssistedGradingBatch.Create(10, [501], "teacher-low", 321, 1, priority: "low");
        var high = AssistedGradingBatch.Create(10, [501], "teacher-high", 321, 1, priority: "high");
        var normal = AssistedGradingBatch.Create(10, [501], "teacher-normal", 321, 1, priority: "normal");
        dbContext.GradingBatches.AddRange(low, high, normal);
        dbContext.GradingItems.AddRange(
            AssistedGradingItem.Create(low.Id, 10, 501, 9001, 101, 0),
            AssistedGradingItem.Create(high.Id, 10, 501, 9002, 102, 0),
            AssistedGradingItem.Create(normal.Id, 10, 501, 9003, 103, 0));
        await dbContext.SaveChangesAsync();

        var claims = await store.ClaimDueBatchesAsync(
            "worker-a",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10),
            maxBatches: 3,
            CancellationToken.None);

        Assert.Equal([high.Id, normal.Id, low.Id], claims.Select(claim => claim.BatchId));
    }

    [Fact]
    public async Task JobStore_ClaimDuePromoveLoteAntigoParaEvitarStarvation()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var agedLow = AssistedGradingBatch.Create(10, [501], "teacher-aged", 321, 1, priority: "low");
        var freshHigh = AssistedGradingBatch.Create(10, [501], "teacher-fresh", 321, 1, priority: "high");
        dbContext.GradingBatches.AddRange(agedLow, freshHigh);
        dbContext.GradingItems.AddRange(
            AssistedGradingItem.Create(agedLow.Id, 10, 501, 9001, 101, 0),
            AssistedGradingItem.Create(freshHigh.Id, 10, 501, 9002, 102, 0));
        dbContext.Entry(agedLow).Property(batch => batch.CreatedAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-31);
        await dbContext.SaveChangesAsync();

        var claims = await store.ClaimDueBatchesAsync(
            "worker-a",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10),
            maxBatches: 2,
            CancellationToken.None);

        Assert.Equal([agedLow.Id, freshHigh.Id], claims.Select(claim => claim.BatchId));
    }

    [Fact]
    public async Task JobStore_LeasePorItemImpedeDuplicacaoRenovaLiberaEContaTentativas()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        dbContext.GradingBatches.Add(batch);
        dbContext.GradingItems.Add(item);
        await dbContext.SaveChangesAsync();

        var claimTime = DateTimeOffset.UtcNow;
        var first = await store.TryClaimItemAsync(
            batch.Id,
            item.Id,
            "worker-a",
            claimTime,
            TimeSpan.FromMinutes(10),
            CancellationToken.None);
        var second = await store.TryClaimItemAsync(
            batch.Id,
            item.Id,
            "worker-b",
            claimTime,
            TimeSpan.FromMinutes(10),
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(1, first!.AttemptCount);
        Assert.Null(second);
        Assert.False(await store.RenewItemLeaseAsync(
            batch.Id,
            item.Id,
            "worker-b",
            claimTime.AddMinutes(1),
            TimeSpan.FromMinutes(10),
            CancellationToken.None));
        Assert.True(await store.RenewItemLeaseAsync(
            batch.Id,
            item.Id,
            "worker-a",
            claimTime.AddMinutes(1),
            TimeSpan.FromMinutes(10),
            CancellationToken.None));
        Assert.False(await store.ReleaseItemLeaseAsync(
            batch.Id,
            item.Id,
            "worker-b",
            claimTime.AddMinutes(2),
            "ignored",
            null,
            CancellationToken.None));
        Assert.True(await store.ReleaseItemLeaseAsync(
            batch.Id,
            item.Id,
            "worker-a",
            claimTime.AddMinutes(2),
            "processing_failed",
            claimTime.AddMinutes(3),
            CancellationToken.None));

        var blockedRetry = await store.TryClaimItemAsync(
            batch.Id,
            item.Id,
            "worker-c",
            claimTime.AddMinutes(2).AddSeconds(30),
            TimeSpan.FromMinutes(10),
            CancellationToken.None);
        Assert.Null(blockedRetry);

        var retry = await store.TryClaimItemAsync(
            batch.Id,
            item.Id,
            "worker-c",
            claimTime.AddMinutes(3),
            TimeSpan.FromMinutes(10),
            CancellationToken.None);
        Assert.NotNull(retry);
        Assert.Equal(2, retry!.AttemptCount);

        var saved = await store.GetItemAsync(item.Id, CancellationToken.None);
        Assert.Equal("worker-c", saved!.LeaseOwner);
        Assert.Equal(2, saved.AttemptCount);
    }

    [Fact]
    public async Task JobStore_ClaimEReleaseEmLoteMantemExclusividadePorJanela()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 32);
        var items = Enumerable.Range(0, 32)
            .Select(index => AssistedGradingItem.Create(
                batch.Id,
                10,
                501,
                9001 + index,
                101 + index,
                0))
            .ToArray();
        dbContext.GradingBatches.Add(batch);
        dbContext.GradingItems.AddRange(items);
        await dbContext.SaveChangesAsync();

        var claimTime = DateTimeOffset.UtcNow;
        var first = await store.TryClaimItemsAsync(
            batch.Id,
            items.Select(item => item.Id).ToArray(),
            "worker-a",
            claimTime,
            TimeSpan.FromMinutes(10),
            CancellationToken.None);
        var second = await store.TryClaimItemsAsync(
            batch.Id,
            items.Select(item => item.Id).ToArray(),
            "worker-b",
            claimTime,
            TimeSpan.FromMinutes(10),
            CancellationToken.None);

        Assert.Equal(32, first.Count);
        Assert.Empty(second);
        Assert.Equal(32, await store.ReleaseItemLeasesAsync(
            batch.Id,
            first,
            "worker-a",
            claimTime.AddMinutes(1),
            errorCode: null,
            nextAttemptAt: null,
            CancellationToken.None));

        var savedItems = await dbContext.GradingItems
            .AsNoTracking()
            .Where(item => item.BatchId == batch.Id)
            .ToArrayAsync();
        Assert.All(savedItems, item =>
        {
            Assert.Null(item.LeaseOwner);
            Assert.Null(item.LeaseUntil);
            Assert.Equal(1, item.AttemptCount);
        });
    }

    [Fact]
    public async Task JobStore_LeasePorItemExpiradoPodeSerRecuperado()
    {
        await using var dbContext = CreateDbContext();
        var store = new GradingReviewRepository(dbContext);
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        dbContext.GradingBatches.Add(batch);
        dbContext.GradingItems.Add(item);
        await dbContext.SaveChangesAsync();

        var claimTime = DateTimeOffset.UtcNow;
        Assert.NotNull(await store.TryClaimItemAsync(
            batch.Id,
            item.Id,
            "worker-a",
            claimTime,
            TimeSpan.FromMinutes(1),
            CancellationToken.None));

        Assert.Equal(1, await store.RecoverExpiredItemLeasesAsync(
            claimTime.AddMinutes(2),
            CancellationToken.None));

        var recovered = await store.GetItemAsync(item.Id, CancellationToken.None);
        Assert.Null(recovered!.LeaseOwner);
        Assert.Null(recovered.LeaseUntil);
        Assert.Equal(claimTime.AddMinutes(2), recovered.NextAttemptAt);
    }

    private static ConnectorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"grading-review-{Guid.NewGuid():N}")
            .Options;

        return new ConnectorDbContext(options);
    }
}
