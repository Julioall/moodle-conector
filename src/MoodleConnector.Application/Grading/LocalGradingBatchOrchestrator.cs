using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Implementação de orquestração inline (stub MVP) sem fila de mensagens real.
/// Marca itens imediatamente sem processamento assíncrono.
/// Quando uma fila real for integrada, substituir por implementação baseada em workers.
/// </summary>
public sealed class LocalGradingBatchOrchestrator(
    IGradingReviewRepository repository,
    IOptions<GradingLimitsOptions> limits,
    ILogger<LocalGradingBatchOrchestrator> logger)
    : IGradingBatchOrchestrator
{
    public async Task EnqueueAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        var batch = await repository.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException("Lote nao encontrado para enfileirar.");

        if (batch.Status is GradingBatchStatus.Processing or GradingBatchStatus.ReadyForReview or GradingBatchStatus.Completed)
        {
            logger.LogDebug(
                "Lote {BatchId} em status {Status} ja processado ou em processamento; enfileiramento ignorado.",
                batchId,
                batch.Status);
            return;
        }

        var maxItems = limits.Value.MaxBatchItems;
        var totalItems = await repository.CountItemsByBatchAsync(batchId, cancellationToken);

        if (totalItems > maxItems)
        {
            throw new InvalidOperationException(
                $"O lote contém {totalItems} itens mas o limite configurado é {maxItems}.");
        }

        logger.LogInformation(
            "Lote {BatchId} com {TotalItems} itens enfileirado para processamento inline (MVP).",
            batchId,
            totalItems);
    }

    public async Task CancelAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        var batch = await repository.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException("Lote nao encontrado para cancelar.");

        if (batch.Status is GradingBatchStatus.Completed or GradingBatchStatus.Cancelled)
        {
            logger.LogDebug(
                "Lote {BatchId} em status {Status} nao pode ser cancelado.",
                batchId,
                batch.Status);
            return;
        }

        batch.Cancel();
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Lote {BatchId} cancelado.", batchId);
    }

    public async Task<GradingBatchOrchestratorStatus> GetStatusAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        var batch = await repository.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException("Lote nao encontrado.");

        return new GradingBatchOrchestratorStatus(
            batch.Id,
            batch.Status,
            batch.TotalItems,
            batch.ProcessedItems,
            batch.ReadyItems,
            batch.BlockedItems,
            batch.FailedItems,
            IsQueued: batch.Status is GradingBatchStatus.Pending or GradingBatchStatus.Processing,
            LastError: null);
    }
}
