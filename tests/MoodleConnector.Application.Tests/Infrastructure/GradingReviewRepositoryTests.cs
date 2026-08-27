using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
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

    private static ConnectorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"grading-review-{Guid.NewGuid():N}")
            .Options;

        return new ConnectorDbContext(options);
    }
}
