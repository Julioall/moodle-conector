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
    IGradingContextBuilder contextBuilder,
    IGradingAnalysisService analysisService,
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

        if (batch.Status is not (GradingBatchStatus.Pending or GradingBatchStatus.Processing))
        {
            logger.LogDebug(
                "Lote {BatchId} em status {Status} nao aceita enfileiramento local; enfileiramento ignorado.",
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

        var items = await repository.ListItemsByBatchAsync(
            batchId,
            page: 1,
            pageSize: maxItems,
            cancellationToken);

        foreach (var item in items.Where(item => item.Status == GradingItemStatus.Pending))
        {
            try
            {
                await ProcessItemAsync(item, cancellationToken);
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

        UpdateBatchCounters(batch, items);
        await repository.SaveChangesAsync(cancellationToken);
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

    private async Task ProcessItemAsync(
        AssistedGradingItem item,
        CancellationToken cancellationToken)
    {
        var context = await contextBuilder.BuildAsync(
            item,
            new GradingContextOptions(
                IncludeRubric: true,
                IncludeSubmissionFiles: true,
                IncludeCourseMaterials: true),
            cancellationToken);

        var readableText = FirstReadableText(context);
        if (string.IsNullOrWhiteSpace(readableText))
        {
            var blockerReason = context.Blockers.FirstOrDefault(b => 
                b.Contains("Submissão sem conteúdo legível", StringComparison.OrdinalIgnoreCase) || 
                b.Contains("Submissão não disponível", StringComparison.OrdinalIgnoreCase))
                ?? "Submissao sem conteudo legivel para correcao assistida.";
            item.BlockAnalysis(blockerReason);
            return;
        }

        // Tenta análise mesmo com contexto incompleto (best-effort):
        // o serviço gera feedback genérico quando critérios ou escala estão ausentes.
        var result = await analysisService.AnalyzeAsync(
            new GradingAnalysisRequest(
                AssignmentName: $"Tarefa {context.AssignmentId}",
                MaxGrade: context.MaxGrade ?? 0m,
                ActivityDescription: context.AssignmentStatement,
                RubricOrCriteria: context.RubricDescription ?? context.Criteria,
                TeacherInstructions: context.TeacherInstructions,
                SubmissionText: readableText,
                FileHashes: context.AttachedFiles
                    .Select(file => file.Sha256)
                    .Where(hash => !string.IsNullOrWhiteSpace(hash))
                    .Select(hash => hash!)
                    .ToArray()),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.FeedbackToStudent))
        {
            foreach (var criterion in result.CriterionAnalysis)
            {
                await repository.AddEvidenceAsync(
                    new GradingEvidence(
                        Guid.NewGuid(),
                        item.Id,
                        criterion.CriterionId,
                        criterion.CriterionText,
                        criterion.MaxPoints,
                        criterion.SuggestedPoints,
                        criterion.EvidenceFound,
                        criterion.Gaps,
                        criterion.TeacherReviewRequired,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            // Quando há blockers de contexto, o rascunho tem confiança zero e nota nula
            // mesmo que o serviço retorne uma sugestão — pois os critérios usados são parciais.
            // Os blockers de contexto são adicionados às notas privadas do professor.
            var confidence = context.Blockers.Count > 0 ? 0m : result.Confidence;
            var suggestedGrade = context.Blockers.Count > 0 ? null : result.SuggestedGrade;
            var teacherNotes = context.Blockers.Count > 0
                ? BuildPreliminaryTeacherNotes(context) + " " + result.PrivateNotesToTeacher
                : result.PrivateNotesToTeacher;

            item.SetDraft(suggestedGrade, confidence, result.FeedbackToStudent, teacherNotes);
            return;
        }

        // Serviço não gerou feedback revisável — bloqueia o item.
        item.BlockAnalysis(
            result.Blocks.Count > 0
                ? string.Join(" ", result.Blocks)
                : "Analise de correcao assistida nao gerou feedback revisavel.");
    }

    private static string? FirstReadableText(GradingContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.SubmissionText))
        {
            return context.SubmissionText;
        }

        return context.AttachedFiles
            .Select(file => file.ExtractedText)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private static string BuildPreliminaryTeacherNotes(GradingContext context)
    {
        return context.Blockers.Count == 0
            ? "Rascunho preliminar sem bloqueadores adicionais."
            : "Rascunho preliminar gerado com pendencias de contexto: " + string.Join(" ", context.Blockers);
    }

    private static void UpdateBatchCounters(
        AssistedGradingBatch batch,
        IReadOnlyList<AssistedGradingItem> items)
    {
        var readyItems = items.Count(item =>
            item.Status is GradingItemStatus.DraftReady or GradingItemStatus.ReadyToCommit);
        var blockedItems = items.Count(item => item.Status == GradingItemStatus.Blocked);
        var failedItems = items.Count(item => item.Status == GradingItemStatus.Failed);
        var processedItems = readyItems + blockedItems + failedItems +
            items.Count(item => item.Status == GradingItemStatus.Committed);

        batch.UpdateCounters(processedItems, readyItems, blockedItems, failedItems);
    }
}
