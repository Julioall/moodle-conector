using Microsoft.Extensions.Logging;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Processa itens individuais de correção assistida.
/// Extraído do antigo LocalGradingBatchOrchestrator para ser reutilizável
/// tanto inline quanto pelo worker assíncrono.
/// </summary>
public sealed class GradingItemProcessor(
    IGradingContextBuilder contextBuilder,
    IGradingAnalysisService analysisService,
    ILogger<GradingItemProcessor> logger)
{
    public async Task ProcessItemAsync(
        AssistedGradingItem item,
        IGradingReviewRepository repository,
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
            logger.LogDebug(
                "Item {GradingItemId} bloqueado: {Reason}",
                item.Id,
                blockerReason);
            item.BlockAnalysis(blockerReason);
            return;
        }

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

            var confidence = context.Blockers.Count > 0 ? 0m : result.Confidence;
            var suggestedGrade = context.Blockers.Count > 0 ? null : result.SuggestedGrade;
            var teacherNotes = context.Blockers.Count > 0
                ? BuildPreliminaryTeacherNotes(context) + " " + result.PrivateNotesToTeacher
                : result.PrivateNotesToTeacher;

            // Appender observações de geração de critérios (separadas de TeacherInstructions)
            if (!string.IsNullOrWhiteSpace(context.CriteriaGenerationNotes))
            {
                teacherNotes = string.IsNullOrWhiteSpace(teacherNotes)
                    ? context.CriteriaGenerationNotes
                    : $"{teacherNotes} {context.CriteriaGenerationNotes}";
            }

            item.SetDraft(suggestedGrade, confidence, result.FeedbackToStudent, teacherNotes);
            return;
        }

        item.BlockAnalysis(
            result.Blocks.Count > 0
                ? string.Join(" ", result.Blocks)
                : "Analise de correcao assistida nao gerou feedback revisavel.");
    }

    public static void UpdateBatchCounters(
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

    /// <summary>
    /// Carrega todos os itens de um lote via paginação.
    /// Reutilizado por múltiplos handlers que precisam iterar o lote inteiro.
    /// </summary>
    public static async Task<IReadOnlyList<AssistedGradingItem>> LoadAllBatchItemsAsync(
        IGradingReviewRepository repository,
        Guid batchId,
        CancellationToken cancellationToken,
        int pageSize = 100)
    {
        var allItems = new List<AssistedGradingItem>();
        var page = 1;
        while (true)
        {
            var pageItems = await repository.ListItemsByBatchAsync(batchId, page, pageSize, cancellationToken);
            allItems.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return allItems;
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
}
