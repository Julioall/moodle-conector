using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<Guid, byte> activeBatches = new();

    private static readonly string WorkerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GradingBatchWorkerService iniciado.");

        if (!IsDurableJobStoreAvailable())
        {
            // Compatibilidade para hosts que ainda não registraram o job store.
            await ResumeInProgressBatchesAsync(stoppingToken);
            await ProcessChannelAsync(stoppingToken);
            return;
        }

        try
        {
            await RecoverExpiredBatchesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível recuperar leases expirados no startup; o polling tentará novamente.");
        }
        // Catch-up: retoma lotes que ficaram em Processing após queda do processo.
        await ResumeInProgressBatchesAsync(stoppingToken);

        try
        {
            await Task.WhenAll(
                ProcessChannelAsync(stoppingToken),
                PollDurableBatchesAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown normal do host.
        }

        logger.LogInformation("GradingBatchWorkerService encerrado.");
    }

    private async Task ProcessChannelAsync(CancellationToken cancellationToken)
    {
        await foreach (var workItem in channel.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessBatchAsync(workItem.BatchId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
    }

    private async Task PollDurableBatchesAsync(CancellationToken cancellationToken)
    {
        var pollSeconds = Math.Clamp(limits.Value.DurableBatchPollSeconds, 1, 300);
        var claimSize = Math.Clamp(limits.Value.DurableBatchClaimSize, 1, 100);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var jobStore = scope.ServiceProvider.GetRequiredService<IGradingBatchJobStore>();
                    var now = DateTimeOffset.UtcNow;
                    var claims = await jobStore.ClaimDueBatchesAsync(
                        WorkerId,
                        now,
                        LeaseDuration,
                        claimSize,
                        cancellationToken);

                    foreach (var claim in claims)
                    {
                        await channel.EnqueueAsync(
                            new GradingBatchWorkItem(claim.BatchId, now, WorkerId),
                            cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Falha no polling durável de lotes de correção; nova tentativa no próximo ciclo.");
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha no polling durável de lotes de correção assistida.");
        }
    }

    private async Task RecoverExpiredBatchesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var jobStore = scope.ServiceProvider.GetRequiredService<IGradingBatchJobStore>();
        var now = DateTimeOffset.UtcNow;
        var recovered = await jobStore.RecoverExpiredBatchLeasesAsync(
            now,
            cancellationToken);
        var recoveredItems = await jobStore.RecoverExpiredItemLeasesAsync(
            now,
            cancellationToken);
        if (recovered > 0)
        {
            logger.LogInformation("Recuperados {Count} lease(s) expirado(s) de lotes de correção.", recovered);
        }

        if (recoveredItems > 0)
        {
            logger.LogInformation("Recuperados {Count} lease(s) expirado(s) de itens de correção.", recoveredItems);
        }
    }

    private bool IsDurableJobStoreAvailable()
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<IGradingBatchJobStore>() is not null;
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
        // O channel pode conter o mesmo lote mais de uma vez (por polling e por
        // enqueue legado). O lease durável protege réplicas distintas; este
        // guard protege também reentrância concorrente dentro do mesmo processo.
        if (!activeBatches.TryAdd(batchId, 0))
        {
            logger.LogDebug("Lote {BatchId} já está em processamento neste worker; duplicata ignorada.", batchId);
            return;
        }

        try
        {
            await ProcessBatchCoreAsync(batchId, cancellationToken);
        }
        finally
        {
            activeBatches.TryRemove(batchId, out _);
        }
    }

    private async Task ProcessBatchCoreAsync(Guid batchId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGradingReviewRepository>();
        var jobStore = scope.ServiceProvider.GetService<IGradingBatchJobStore>();
        var processor = scope.ServiceProvider.GetRequiredService<GradingItemProcessor>();
        var maxItems = limits.Value.MaxBatchItems;

        if (jobStore is not null && await jobStore.TryClaimBatchAsync(
                batchId,
                WorkerId,
                DateTimeOffset.UtcNow,
                LeaseDuration,
                cancellationToken) is null)
        {
            logger.LogDebug("Lote {BatchId} já possui claim ativo ou não está pronto; ignorado.", batchId);
            return;
        }

        var batch = await repository.GetBatchAsync(batchId, cancellationToken);
        if (batch is null)
        {
            logger.LogWarning("Lote {BatchId} nao encontrado no worker; ignorado.", batchId);
            return;
        }

        if (batch.Status is GradingBatchStatus.Cancelled or GradingBatchStatus.Completed)
        {
            if (jobStore is not null)
            {
                await jobStore.ReleaseBatchLeaseAsync(
                    batchId,
                    WorkerId,
                    DateTimeOffset.UtcNow,
                    errorCode: null,
                    nextAttemptAt: null,
                    cancellationToken);
            }

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

        var claimedItemIds = new List<Guid>(pendingItems.Length);
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
                if (jobStore is not null)
                {
                    await jobStore.ReleaseBatchLeaseAsync(
                        batchId,
                        WorkerId,
                        DateTimeOffset.UtcNow,
                        errorCode: null,
                        nextAttemptAt: null,
                        cancellationToken);
                }

                logger.LogInformation(
                    "Lote {BatchId} cancelado durante processamento; interrompendo worker.",
                    batchId);
                break;
            }

            try
            {
                if (jobStore is not null && !await jobStore.RenewBatchLeaseAsync(
                        batchId,
                        WorkerId,
                        DateTimeOffset.UtcNow,
                        LeaseDuration,
                        cancellationToken))
                {
                    logger.LogWarning("Lease do lote {BatchId} expirou durante o processamento.", batchId);
                    return;
                }

                if (jobStore is not null && await jobStore.TryClaimItemAsync(
                        batchId,
                        item.Id,
                        WorkerId,
                        DateTimeOffset.UtcNow,
                        LeaseDuration,
                        cancellationToken) is null)
                {
                    logger.LogDebug(
                        "Item {GradingItemId} do lote {BatchId} já possui claim ativo ou não está pronto; ignorado.",
                        item.Id,
                        batchId);
                    continue;
                }

                if (jobStore is not null)
                {
                    claimedItemIds.Add(item.Id);
                }

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

                if (jobStore is not null)
                {
                    var checkpointed = await jobStore.UpdateBatchCheckpointAsync(
                        batchId,
                        WorkerId,
                        item.Id,
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                    if (!checkpointed)
                    {
                        logger.LogWarning("Lease do lote {BatchId} expirou antes do checkpoint; descartando alterações locais.", batchId);
                        return;
                    }
                }
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
        if (jobStore is not null && !await jobStore.RenewBatchLeaseAsync(
                batchId,
                WorkerId,
                DateTimeOffset.UtcNow,
                LeaseDuration,
                cancellationToken))
        {
            logger.LogWarning("Lease do lote {BatchId} expirou antes de salvar os contadores.", batchId);
            return;
        }

        var allItems = await GradingItemProcessor.LoadAllBatchItemsAsync(
            repository,
            batchId,
            cancellationToken,
            maxItems);

        GradingItemProcessor.UpdateBatchCounters(batch, allItems);
        await repository.SaveChangesAsync(cancellationToken);

        if (jobStore is not null)
        {
            foreach (var itemId in claimedItemIds)
            {
                await jobStore.ReleaseItemLeaseAsync(
                    batchId,
                    itemId,
                    WorkerId,
                    DateTimeOffset.UtcNow,
                    errorCode: null,
                    nextAttemptAt: null,
                    cancellationToken);
            }

            await jobStore.ReleaseBatchLeaseAsync(
                batchId,
                WorkerId,
                DateTimeOffset.UtcNow,
                errorCode: null,
                nextAttemptAt: null,
                cancellationToken);
        }

        logger.LogInformation(
            "Lote {BatchId} processado: {ReadyItems} prontos, {BlockedItems} bloqueados, {FailedItems} falhos.",
            batchId,
            batch.ReadyItems,
            batch.BlockedItems,
            batch.FailedItems);
    }

    private TimeSpan LeaseDuration => TimeSpan.FromMinutes(
        Math.Clamp(limits.Value.BatchLeaseMinutes, 1, 120));
}
