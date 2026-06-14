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

    private static ConnectorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"grading-review-{Guid.NewGuid():N}")
            .Options;

        return new ConnectorDbContext(options);
    }
}
