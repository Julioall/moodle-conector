using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain.Grading;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class GradingReviewRepositoryTests
{
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
