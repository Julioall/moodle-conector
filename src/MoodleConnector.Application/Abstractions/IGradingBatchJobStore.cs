using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Estado durável de execução do lote. O channel em memória pode acelerar o
/// despacho, mas nunca substitui estas operações de claim/lease.
/// </summary>
public interface IGradingBatchJobStore
{
    Task<IReadOnlyList<GradingBatchLeaseClaim>> ClaimDueBatchesAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int maxBatches,
        CancellationToken cancellationToken);

    Task<GradingBatchLeaseClaim?> TryClaimBatchAsync(
        Guid batchId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RenewBatchLeaseAsync(
        Guid batchId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> ReleaseBatchLeaseAsync(
        Guid batchId,
        string workerId,
        DateTimeOffset now,
        string? errorCode,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken);

    Task<bool> UpdateBatchCheckpointAsync(
        Guid batchId,
        string workerId,
        Guid itemId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> RecoverExpiredBatchLeasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record GradingBatchLeaseClaim(
    Guid BatchId,
    string WorkerId,
    DateTimeOffset LeaseUntil,
    int AttemptCount);
