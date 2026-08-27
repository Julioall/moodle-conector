using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Background worker que consome lotes da GradingBatchChannel e processa
/// os itens pendentes de forma assíncrona, sem travar o chat MCP.
/// Ao iniciar, retoma lotes que ficaram em Processing após queda do processo.
/// </summary>
public sealed class GradingBatchWorkerService(
    GradingBatchChannel channel,
    IServiceScopeFactory scopeFactory,
    IOptions<GradingLimitsOptions> limits,
    ILogger<GradingBatchWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GradingBatchWorkerService iniciado.");

        // Catch-up: retoma lotes que ficaram em Processing após queda do processo.
        await ResumeInProgressBatchesAsync(stoppingToken);

        await foreach (var workItem in channel.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(workItem.BatchId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Falha fatal ao processar lote {BatchId} do worker.",
                    workItem.BatchId);
            }
        }

        logger.LogInformation("GradingBatchWorkerService encerrado.");
    }

    private async Task ResumeInProgressBatchesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IGradingReviewRepository>();
            var inProgressBatches = await repository.ListBatchesByStatusAsync(
                GradingBatchStatus.Processing,
                cancellationToken);

            if (inProgressBatches.Count == 0)
            {
                return;
            }

            logger.LogInformation(
                "Retomando {Count} lote(s) em Processing encontrado(s) no startup.",
                inProgressBatches.Count);

            foreach (var batch in inProgressBatches)
            {
                await channel.EnqueueAsync(
                    new GradingBatchWorkItem(batch.Id, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown durante catch-up — normal.
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Falha ao tentar retomar lotes em Processing no startup. Lotes serao processados quando reenfileirados manualmente.");
        }
    }

    private async Task ProcessBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGradingReviewRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<GradingItemProcessor>();
        var maxItems = limits.Value.MaxBatchItems;

        var batch = await repository.GetBatchAsync(batchId, cancellationToken);
        if (batch is null)
        {
            logger.LogWarning("Lote {BatchId} nao encontrado no worker; ignorado.", batchId);
            return;
        }

        if (batch.Status is GradingBatchStatus.Cancelled or GradingBatchStatus.Completed)
        {
            logger.LogDebug(
                "Lote {BatchId} em status {Status}; processamento ignorado.",
                batchId,
                batch.Status);
            return;
        }

        var items = await GradingItemProcessor.LoadAllBatchItemsAsync(
            repository,
            batchId,
            cancellationToken,
            maxItems);

        var pendingItems = items.Where(item => item.Status == GradingItemStatus.Pending).ToArray();

        logger.LogInformation(
            "Processando lote {BatchId}: {PendingCount} itens pendentes de {TotalCount} total.",
            batchId,
            pendingItems.Length,
            items.Count);

        foreach (var item in pendingItems)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Verificar se lote foi cancelado durante processamento.
            var currentBatch = await repository.GetBatchAsync(batchId, cancellationToken);
            if (currentBatch?.Status == GradingBatchStatus.Cancelled)
            {
                logger.LogInformation(
                    "Lote {BatchId} cancelado durante processamento; interrompendo worker.",
                    batchId);
                break;
            }

            try
            {
                await processor.ProcessItemAsync(
                    item,
                    repository,
                    cancellationToken,
                    batch.TeacherInstructions,
                    new GradingContextOptions(
                        batch.IncludeRubric,
                        batch.IncludeSubmissionFiles,
                        batch.IncludeCourseMaterials,
                        batch.TeacherInstructions));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Falha recuperavel ao processar item {GradingItemId} do lote {BatchId}.",
                    item.Id,
                    batchId);
                item.MarkAnalysisFailed($"Falha ao processar este item de correcao assistida: {ex.Message}");
            }
        }

        // Recarregar todos para contadores atualizados.
        var allItems = await GradingItemProcessor.LoadAllBatchItemsAsync(
            repository,
            batchId,
            cancellationToken,
            maxItems);

        GradingItemProcessor.UpdateBatchCounters(batch, allItems);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Lote {BatchId} processado: {ReadyItems} prontos, {BlockedItems} bloqueados, {FailedItems} falhos.",
            batchId,
            batch.ReadyItems,
            batch.BlockedItems,
            batch.FailedItems);
    }
}
