using System.Threading.Channels;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Canal in-process para enfileirar lotes de correção assistida para processamento assíncrono.
/// Singleton: compartilhado entre o orchestrator (produtor) e o worker (consumidor).
/// </summary>
public sealed class GradingBatchChannel
{
    private readonly Channel<GradingBatchWorkItem> _channel =
        Channel.CreateBounded<GradingBatchWorkItem>(new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(GradingBatchWorkItem workItem, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    public IAsyncEnumerable<GradingBatchWorkItem> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public int PendingCount => _channel.Reader.Count;
}

public sealed record GradingBatchWorkItem(
    Guid BatchId,
    DateTimeOffset EnqueuedAt,
    string? LeaseOwner = null);
