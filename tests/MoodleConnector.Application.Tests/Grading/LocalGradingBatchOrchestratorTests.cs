using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class LocalGradingBatchOrchestratorTests
{
    private static IOptions<GradingLimitsOptions> DefaultLimits(int maxItems = 400)
    {
        return Options.Create(new GradingLimitsOptions { MaxBatchItems = maxItems });
    }

    [Fact]
    public async Task EnqueueAsync_ComLoteValido_NaoLancaExcecao()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = CreateSut(repository);

        await sut.EnqueueAsync(batch.Id, CancellationToken.None);
    }

    [Fact]
    public async Task EnqueueAsync_ComArtefatoExtraido_GeraRascunhoEAtualizaContadores()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "submission_file",
            "entrega.txt",
            "text/plain",
            "sha-1",
            120,
            "succeeded",
            "Texto extraido da entrega com evidencias suficientes para parecer preliminar.",
            SummaryRef: null,
            new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero)));
        var sut = CreateSut(repository);

        await sut.EnqueueAsync(batch.Id, CancellationToken.None);

        Assert.Equal(GradingItemStatus.DraftReady, item.Status);
        Assert.Equal(GradingReviewStatus.NotReviewed, item.ReviewStatus);
        Assert.Equal(GradingCommitStatus.NotReady, item.CommitStatus);
        Assert.Null(item.SuggestedGrade);
        Assert.Equal(0m, item.Confidence);
        Assert.Contains("parecer preliminar", item.DraftFeedback, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GradingBatchStatus.ReadyForReview, batch.Status);
        Assert.Equal(1, batch.ProcessedItems);
        Assert.Equal(1, batch.ReadyItems);
        Assert.Equal(0, batch.BlockedItems);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task EnqueueAsync_SemTextoExtraido_BloqueiaItemEAtualizaContadores()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        await repository.AddItemAsync(item, CancellationToken.None);
        var sut = CreateSut(repository);

        await sut.EnqueueAsync(batch.Id, CancellationToken.None);

        Assert.Equal(GradingItemStatus.Blocked, item.Status);
        Assert.Contains("conteudo legivel", item.DraftFeedback, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GradingBatchStatus.ReadyForReview, batch.Status);
        Assert.Equal(1, batch.ProcessedItems);
        Assert.Equal(0, batch.ReadyItems);
        Assert.Equal(1, batch.BlockedItems);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task EnqueueAsync_ComBatchIdVazio_LancaArgumentException()
    {
        var sut = new LocalGradingBatchOrchestrator(
            new FakeGradingReviewRepository(),
            DefaultLimits(),
            new FakeGradingContextBuilder(new FakeGradingReviewRepository()),
            new FakeGradingAnalysisService(),
            NullLogger<LocalGradingBatchOrchestrator>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.EnqueueAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task EnqueueAsync_ComTotalItensSuperandoLimite_LancaInvalidOperationException()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 5);
        await repository.AddBatchAsync(batch, CancellationToken.None);

        for (var i = 0; i < 5; i++)
        {
            var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9000 + i, 100 + i, 0);
            await repository.AddItemAsync(item, CancellationToken.None);
        }

        var sut = new LocalGradingBatchOrchestrator(
            repository,
            DefaultLimits(maxItems: 2),
            new FakeGradingContextBuilder(repository),
            new FakeGradingAnalysisService(),
            NullLogger<LocalGradingBatchOrchestrator>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.EnqueueAsync(batch.Id, CancellationToken.None));

        Assert.Contains("limite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAsync_ComLotePendente_AlteraStatusParaCancelled()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = CreateSut(repository);

        await sut.CancelAsync(batch.Id, CancellationToken.None);

        Assert.Equal(GradingBatchStatus.Cancelled, batch.Status);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task CancelAsync_ComLoteJaCancelado_NaoSalvaAlteracoes()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        batch.Cancel();
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = CreateSut(repository);

        await sut.CancelAsync(batch.Id, CancellationToken.None);

        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task GetStatusAsync_RetornaStatusAtualDoLote()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 3);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = CreateSut(repository);

        var status = await sut.GetStatusAsync(batch.Id, CancellationToken.None);

        Assert.Equal(batch.Id, status.BatchId);
        Assert.Equal(GradingBatchStatus.Pending, status.BatchStatus);
        Assert.Equal(3, status.TotalItems);
        Assert.True(status.IsQueued);
    }

    private sealed class FakeGradingReviewRepository : IGradingReviewRepository
    {
        public List<AssistedGradingBatch> Batches { get; } = [];
        public List<AssistedGradingItem> Items { get; } = [];
        public List<GradingArtifact> Artifacts { get; } = [];
        public int SaveChangesCount { get; private set; }

        public Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }

        public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(Batches.SingleOrDefault(b => b.Id == id));

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

        public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(Items.SingleOrDefault(i => i.Id == id));

        public Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
            Guid batchId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var result = Items.Where(i => i.BatchId == batchId).Take(pageSize).ToArray();
            return Task.FromResult<IReadOnlyList<AssistedGradingItem>>(result);
        }

        public Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken)
            => Task.FromResult(Items.Count(i => i.BatchId == batchId));

        public Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(
            Guid gradingItemId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GradingArtifact>>(Artifacts
                .Where(artifact => artifact.GradingItemId == gradingItemId)
                .ToArray());

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }

    private static LocalGradingBatchOrchestrator CreateSut(
        FakeGradingReviewRepository repository,
        int maxItems = 400)
    {
        return new LocalGradingBatchOrchestrator(
            repository,
            DefaultLimits(maxItems),
            new FakeGradingContextBuilder(repository),
            new FakeGradingAnalysisService(),
            NullLogger<LocalGradingBatchOrchestrator>.Instance);
    }

    private sealed class FakeGradingContextBuilder(FakeGradingReviewRepository repository) : IGradingContextBuilder
    {
        public async Task<GradingContext> BuildAsync(
            AssistedGradingItem item,
            GradingContextOptions options,
            CancellationToken cancellationToken)
        {
            var artifacts = await repository.ListArtifactsByItemAsync(item.Id, cancellationToken);
            var text = artifacts
                .Where(artifact => artifact.ExtractionStatus == "succeeded")
                .Select(artifact => artifact.ExtractedTextRef)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return GradingContext.Build(
                item.Id,
                item.BatchId,
                item.CourseId.ToString(),
                item.AssignmentId.ToString(),
                item.SubmissionId?.ToString(),
                item.MoodleUserId.ToString(),
                assignmentStatement: null,
                criteria: null,
                rubricDescription: null,
                maxGrade: null,
                gradeScale: null,
                submissionText: text,
                attachedFiles: string.IsNullOrWhiteSpace(text)
                    ? []
                    : [new GradingFileInfo("entrega.txt", "text/plain", 120, "sha-1", text, true)],
                courseMaterials: null,
                teacherInstructions: options.TeacherInstructions);
        }
    }

    private sealed class FakeGradingAnalysisService : IGradingAnalysisService
    {
        public Task<GradingAnalysisResult> AnalyzeAsync(
            GradingAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new GradingAnalysisResult(
                SuggestedGrade: null,
                Confidence: 0m,
                AnalysisStatus.BlockedMissingCriteria,
                "Parecer preliminar gerado para revisao do professor.",
                PrivateNotesToTeacher: "Sem criterios.",
                CriterionAnalysis: [],
                Blocks: ["Criterios ausentes."]));
        }
    }
}
