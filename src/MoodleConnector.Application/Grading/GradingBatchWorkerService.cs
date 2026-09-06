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
    private const int ItemClaimWindowSize = 16;
    private const int CheckpointInterval = 25;
    private readonly ConcurrentDictionary<Guid, byte> activeBatches = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> connectionGates = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string WorkerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("GradingBatchWorkerService iniciado.");

        if (!IsDurableJobStoreAvailable())
        {
            // Compatibilidade para hosts que ainda não registraram o job store.
            await ResumeInProgressBatchesAsync(stoppingToken);
            await ProcessChannelConsumersAsync(stoppingToken);
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
                ProcessChannelConsumersAsync(stoppingToken),
                PollDurableBatchesAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown normal do host.
        }

        logger.LogInformation("GradingBatchWorkerService encerrado.");
    }

    private Task ProcessChannelConsumersAsync(CancellationToken cancellationToken)
    {
        var consumerCount = Math.Clamp(limits.Value.BatchWorkerConcurrency, 1, 32);
        return Task.WhenAll(Enumerable.Range(0, consumerCount)
            .Select(_ => ProcessChannelAsync(cancellationToken)));
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
        var ingestionService = scope.ServiceProvider.GetRequiredService<IGradingArtifactIngestionService>();
        var executionContext = scope.ServiceProvider.GetService<IConnectorExecutionContext>();
        var connectionSelection = scope.ServiceProvider.GetService<IMoodleConnectionSelection>();
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

        var connectionKey = ResolveConnectionKey(batch);
        var connectionGate = connectionGates.GetOrAdd(
            connectionKey,
            _ => new SemaphoreSlim(
                Math.Clamp(limits.Value.BatchWorkerPerConnectionConcurrency, 1, 16),
                Math.Clamp(limits.Value.BatchWorkerPerConnectionConcurrency, 1, 16)));
        await connectionGate.WaitAsync(cancellationToken);

        var enteredExecutionContext = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(batch.ConnectorClientId) && executionContext is not null)
            {
                executionContext.Enter(batch.ConnectorClientId, batch.CreatedBySubject, null);
                enteredExecutionContext = true;
                if (connectionSelection is not null)
                {
                    connectionSelection.Alias = batch.ConnectionAlias;
                }
            }
        }
        catch
        {
            if (enteredExecutionContext)
            {
                if (connectionSelection is not null)
                {
                    connectionSelection.Alias = null;
                }
                executionContext!.Clear();
            }
            connectionGate.Release();
            throw;
        }

        try
        {
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

            // Artifact references are immutable for a batch item in the
            // normal path. Let ingestion hydrate them once for the entire
            // child batch instead of issuing two artifact SELECTs per item.
            await ingestionService.PrepareBatchAsync(
                batch,
                pendingItems.Select(item => item.Id).ToArray(),
                cancellationToken);
            await processor.PrepareBatchAsync(
                batch,
                pendingItems,
                cancellationToken);

            logger.LogInformation(
                "Processando lote {BatchId}: {PendingCount} itens pendentes de {TotalCount} total.",
                batchId,
                pendingItems.Length,
                items.Count);

            var processorCheckpointsSinceSave = 0;
            var itemsSinceOperationalCheck = 0;
            var itemsSinceBatchCheckpoint = 0;
            var lastBatchCheckpointItemId = Guid.Empty;
            var batchCancelled = false;

            async Task CheckpointProcessorAsync(CancellationToken checkpointCancellationToken)
            {
                // Context and analysis transitions stay on the tracked item,
                // but flushing every transition turns a 10k run into tens of
                // thousands of database round trips. Periodic and final saves
                // bound restart loss without multiplying database pressure.
                processorCheckpointsSinceSave++;
                if (processorCheckpointsSinceSave >= CheckpointInterval)
                {
                    await repository.SaveChangesAsync(checkpointCancellationToken);
                    processorCheckpointsSinceSave = 0;
                }
            }

            // Claims are deliberately windowed. Claiming an entire 400-item
            // batch at once would let early leases expire while later items
            // wait behind slow AI calls, allowing another replica to take the
            // same work. Sixteen items keeps the claim horizon bounded while
            // reducing claim/release round trips by an order of magnitude.
            foreach (var itemWindow in pendingItems.Chunk(ItemClaimWindowSize))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var windowClaimedItemIds = jobStore is null
                    ? itemWindow.Select(item => item.Id).ToHashSet()
                    : (await jobStore.TryClaimItemsAsync(
                        batchId,
                        itemWindow.Select(item => item.Id).ToArray(),
                        WorkerId,
                        DateTimeOffset.UtcNow,
                        LeaseDuration,
                        cancellationToken)).ToHashSet();

                foreach (var item in itemWindow.Where(item => windowClaimedItemIds.Contains(item.Id)))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    itemsSinceOperationalCheck++;
                    // Verificar cancelamento em intervalos amortizados. O
                    // lease do lote continua protegendo contra outra réplica;
                    // a leitura por item era um N+1 desnecessário.
                    if (itemsSinceOperationalCheck >= CheckpointInterval)
                    {
                        itemsSinceOperationalCheck = 0;
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
                            batchCancelled = true;
                            break;
                        }
                    }

                    async Task ProcessItemCoreAsync(CancellationToken itemCancellationToken)
                    {
                        item.MarkProcessingStage(GradingProcessingStage.Ingestion);

                        await ingestionService.IngestPendingAsync(
                            batch,
                            item,
                            itemCancellationToken);

                        await processor.ProcessItemAsync(
                            item,
                            repository,
                            itemCancellationToken,
                            batch.TeacherInstructions,
                            new GradingContextOptions(
                                batch.IncludeRubric,
                                batch.IncludeSubmissionFiles,
                                batch.IncludeCourseMaterials,
                                batch.TeacherInstructions),
                            checkpointAsync: CheckpointProcessorAsync);
                        item.MarkProcessingStage(
                            item.Status == GradingItemStatus.AwaitingAiAnalysis
                                ? GradingProcessingStage.Analysis
                                : item.Status is GradingItemStatus.Blocked or GradingItemStatus.Failed
                                    ? GradingProcessingStage.Failed
                                    : GradingProcessingStage.Completed);
                    }

                    try
                    {
                        if (jobStore is null)
                        {
                            await ProcessItemCoreAsync(cancellationToken);
                        }
                        else if (!await ProcessItemWithLeaseHeartbeatAsync(
                                     jobStore,
                                     batchId,
                                     item.Id,
                                     itemWindow
                                         .Where(candidate =>
                                             windowClaimedItemIds.Contains(candidate.Id) &&
                                             candidate.Status == GradingItemStatus.Pending)
                                         .Select(candidate => candidate.Id)
                                         .ToArray(),
                                     cancellationToken,
                                     ProcessItemCoreAsync,
                                     getRenewableItemIds: () => itemWindow
                                         .Where(candidate =>
                                             windowClaimedItemIds.Contains(candidate.Id) &&
                                             candidate.Status == GradingItemStatus.Pending)
                                         .Select(candidate => candidate.Id)
                                         .ToArray()))
                        {
                            logger.LogWarning(
                                "Lease do item {GradingItemId} ou do lote {BatchId} foi perdido; descartando o restante deste worker.",
                                item.Id,
                                batchId);
                            return;
                        }

                        lastBatchCheckpointItemId = item.Id;
                        itemsSinceBatchCheckpoint++;
                        if (jobStore is not null && itemsSinceBatchCheckpoint >= CheckpointInterval)
                        {
                            // A checkpoint is also the amortized heartbeat
                            // for fast items. If every item finishes before
                            // the per-item timer ticks, renewing only inside
                            // long calls would let the 15-minute batch lease
                            // expire during a long 10k run.
                            var batchLeaseRenewed = await jobStore.RenewBatchLeaseAsync(
                                batchId,
                                WorkerId,
                                DateTimeOffset.UtcNow,
                                LeaseDuration,
                                cancellationToken);
                            if (!batchLeaseRenewed)
                            {
                                logger.LogWarning(
                                    "Lease do lote {BatchId} expirou antes do heartbeat de checkpoint; descartando alterações locais.",
                                    batchId);
                                return;
                            }

                            var checkpointed = await jobStore.UpdateBatchCheckpointAsync(
                                batchId,
                                WorkerId,
                                lastBatchCheckpointItemId,
                                DateTimeOffset.UtcNow,
                                cancellationToken);
                            if (!checkpointed)
                            {
                                logger.LogWarning(
                                    "Lease do lote {BatchId} expirou antes do checkpoint; descartando alterações locais.",
                                    batchId);
                                return;
                            }

                            itemsSinceBatchCheckpoint = 0;
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
                        item.MarkProcessingStage(GradingProcessingStage.Failed);
                    }
                }

                // The window's leases are no longer needed after its items
                // have been processed. A bulk release avoids one UPDATE per
                // item while keeping interrupted windows recoverable by TTL.
                if (jobStore is not null && windowClaimedItemIds.Count > 0)
                {
                    await jobStore.ReleaseItemLeasesAsync(
                        batchId,
                        windowClaimedItemIds,
                        WorkerId,
                        DateTimeOffset.UtcNow,
                        errorCode: null,
                        nextAttemptAt: null,
                        cancellationToken);
                }

                if (batchCancelled)
                {
                    break;
                }
            }

            if (jobStore is not null && lastBatchCheckpointItemId != Guid.Empty)
            {
                var checkpointed = await jobStore.UpdateBatchCheckpointAsync(
                    batchId,
                    WorkerId,
                    lastBatchCheckpointItemId,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                if (!checkpointed)
                {
                    logger.LogWarning("Lease do lote {BatchId} expirou antes do checkpoint final.", batchId);
                    return;
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
        finally
        {
            if (enteredExecutionContext)
            {
                if (connectionSelection is not null)
                {
                    connectionSelection.Alias = null;
                }
                executionContext!.Clear();
            }

            connectionGate.Release();
        }
    }

    private static string ResolveConnectionKey(AssistedGradingBatch batch) =>
        !string.IsNullOrWhiteSpace(batch.MoodleConnectionId)
            ? $"connection:{batch.MoodleConnectionId.Trim()}"
            : $"client:{(string.IsNullOrWhiteSpace(batch.ConnectorClientId) ? "default" : batch.ConnectorClientId)}" +
              $":alias:{(string.IsNullOrWhiteSpace(batch.ConnectionAlias) ? "default" : batch.ConnectionAlias)}";

    private async Task<bool> ProcessItemWithLeaseHeartbeatAsync(
        IGradingBatchJobStore jobStore,
        Guid batchId,
        Guid itemId,
        IReadOnlyCollection<Guid> windowItemIds,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> work,
        Func<bool>? shouldRenewItem = null,
        Func<IReadOnlyCollection<Guid>>? getRenewableItemIds = null)
    {
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var heartbeat = MaintainLeaseHeartbeatAsync(
            jobStore,
            batchId,
            itemId,
            windowItemIds,
            leaseCancellation,
            () => Interlocked.Exchange(ref leaseLost, 1),
            shouldRenewItem,
            getRenewableItemIds);

        try
        {
            await work(leaseCancellation.Token);
            return Volatile.Read(ref leaseLost) == 0;
        }
        finally
        {
            leaseCancellation.Cancel();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
                // Cancellation belongs to the linked work token and is
                // observed by the caller when the item operation is aborted.
            }
        }
    }

    private async Task MaintainLeaseHeartbeatAsync(
        IGradingBatchJobStore jobStore,
        Guid batchId,
        Guid itemId,
        IReadOnlyCollection<Guid> windowItemIds,
        CancellationTokenSource leaseCancellation,
        Action markLeaseLost,
        Func<bool>? shouldRenewItem,
        Func<IReadOnlyCollection<Guid>>? getRenewableItemIds)
    {
        var interval = TimeSpan.FromTicks(Math.Max(
            TimeSpan.FromSeconds(5).Ticks,
            LeaseDuration.Ticks / 3));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(leaseCancellation.Token))
            {
                var now = DateTimeOffset.UtcNow;
                if (!await jobStore.RenewBatchLeaseAsync(
                        batchId,
                        WorkerId,
                        now,
                        LeaseDuration,
                        leaseCancellation.Token))
                {
                    logger.LogWarning(
                        "Lease do lote {BatchId} expirou durante o processamento do item {GradingItemId}.",
                        batchId,
                        itemId);
                    markLeaseLost();
                    leaseCancellation.Cancel();
                    return;
                }

                // Keep queued items in this claim window owned by the same
                // worker. Without this bulk heartbeat, a slow first item
                // could let later queued items expire and be claimed by a
                // second replica before this worker reaches them.
                var renewableItemIds = getRenewableItemIds?.Invoke() ?? windowItemIds;
                if (renewableItemIds.Count > 0)
                {
                    var renewedItemCount = await jobStore.RenewItemLeasesAsync(
                        batchId,
                        renewableItemIds,
                        WorkerId,
                        now,
                        LeaseDuration,
                        leaseCancellation.Token);
                    if (renewedItemCount < renewableItemIds.Count)
                    {
                        // A short renewal means another replica acquired at
                        // least one still-pending item after its lease
                        // expired; abort before this worker can write a
                        // duplicate.
                        logger.LogWarning(
                            "A janela do lote {BatchId} perdeu {LostCount} claim(s) durante o item {GradingItemId}.",
                            batchId,
                            renewableItemIds.Count - renewedItemCount,
                            itemId);
                        markLeaseLost();
                        leaseCancellation.Cancel();
                        return;
                    }
                }

                // Once the processor has moved the item out of Pending, its
                // lease no longer needs renewal. Avoid treating that normal
                // transition as a lost lease when the heartbeat races the
                // final item update.
                if (shouldRenewItem is not null && !shouldRenewItem())
                {
                    continue;
                }

                if (!await jobStore.RenewItemLeaseAsync(
                        batchId,
                        itemId,
                        WorkerId,
                        now,
                        LeaseDuration,
                        leaseCancellation.Token))
                {
                    logger.LogWarning(
                        "Lease do item {GradingItemId} expirou durante o processamento do lote {BatchId}.",
                        itemId,
                        batchId);
                    markLeaseLost();
                    leaseCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
        {
            // Normal completion or cancellation of the item work.
        }
        catch (Exception exception)
        {
            // A heartbeat failure is safety-critical: stop this worker rather
            // than risk writing after another replica has acquired the lease.
            logger.LogWarning(
                exception,
                "Falha ao renovar lease do lote {BatchId}/item {GradingItemId}; interrompendo o processamento local.",
                batchId,
                itemId);
            markLeaseLost();
            leaseCancellation.Cancel();
        }
    }

    private TimeSpan LeaseDuration => TimeSpan.FromMinutes(
        Math.Clamp(limits.Value.BatchLeaseMinutes, 1, 120));
}
