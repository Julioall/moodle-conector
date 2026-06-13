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
        var sut = new LocalGradingBatchOrchestrator(repository, DefaultLimits(), NullLogger<LocalGradingBatchOrchestrator>.Instance);

        await sut.EnqueueAsync(batch.Id, CancellationToken.None);
    }

    [Fact]
    public async Task EnqueueAsync_ComBatchIdVazio_LancaArgumentException()
    {
        var sut = new LocalGradingBatchOrchestrator(
            new FakeGradingReviewRepository(),
            DefaultLimits(),
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
        var sut = new LocalGradingBatchOrchestrator(repository, DefaultLimits(), NullLogger<LocalGradingBatchOrchestrator>.Instance);

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
        var sut = new LocalGradingBatchOrchestrator(repository, DefaultLimits(), NullLogger<LocalGradingBatchOrchestrator>.Instance);

        await sut.CancelAsync(batch.Id, CancellationToken.None);

        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task GetStatusAsync_RetornaStatusAtualDoLote()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 3);
        await repository.AddBatchAsync(batch, CancellationToken.None);
        var sut = new LocalGradingBatchOrchestrator(repository, DefaultLimits(), NullLogger<LocalGradingBatchOrchestrator>.Instance);

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
            => Task.FromResult<IReadOnlyList<GradingArtifact>>([]);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }
}
