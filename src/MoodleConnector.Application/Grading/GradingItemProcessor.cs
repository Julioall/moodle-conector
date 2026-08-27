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
        CancellationToken cancellationToken,
        string? teacherInstructions = null)
    {
        var context = await contextBuilder.BuildAsync(
            item,
            new GradingContextOptions(
                IncludeRubric: true,
                IncludeSubmissionFiles: true,
                IncludeCourseMaterials: true,
                TeacherInstructions: teacherInstructions),
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

        // --- Fluxo IA-first: o serviço de análise nunca gera nota/feedback heurístico ---

        // Itens bloqueados (texto vazio, enunciado insuficiente)
        if (result.Blocks.Count > 0)
        {
            item.BlockAnalysis(string.Join(" ", result.Blocks));
            return;
        }

        // Itens prontos para IA (pré-validação diagnóstica aprovada)
        if (result.AnalysisStatus == AnalysisStatus.AwaitingAiAnalysis)
        {
            var diagnosticNotes = result.PrivateNotesToTeacher;

            // Appender observações de geração de critérios (separadas de TeacherInstructions)
            if (!string.IsNullOrWhiteSpace(context.CriteriaGenerationNotes))
            {
                diagnosticNotes = string.IsNullOrWhiteSpace(diagnosticNotes)
                    ? context.CriteriaGenerationNotes
                    : $"{diagnosticNotes} {context.CriteriaGenerationNotes}";
            }

            item.MarkAwaitingAiAnalysis(diagnosticNotes);
            return;
        }

        // Fallback: resultado inesperado — bloquear com motivo genérico
        item.BlockAnalysis(
            "Analise de correcao assistida nao gerou resultado processavel. Verifique os dados da atividade.");
    }

    public static void UpdateBatchCounters(
        AssistedGradingBatch batch,
        IReadOnlyList<AssistedGradingItem> items)
    {
        // ReadyItems representa somente rascunhos que aguardam revisão humana.
        // Itens ReadyToCommit são launchPending e não devem inflar essa métrica.
        var readyItems = items.Count(item => item.Status == GradingItemStatus.DraftReady);
        var blockedItems = items.Count(item => item.Status == GradingItemStatus.Blocked);
        var failedItems = items.Count(item => item.Status == GradingItemStatus.Failed);
        var processedItems = items.Count(item => item.Status is
            GradingItemStatus.DraftReady or
            GradingItemStatus.ReadyToCommit or
            GradingItemStatus.Committed or
            GradingItemStatus.Blocked or
            GradingItemStatus.Failed);

        batch.UpdateCounters(processedItems, readyItems, blockedItems, failedItems);

        if (items.Count > 0 && items.All(item => item.Status == GradingItemStatus.Committed))
        {
            batch.MarkCompleted();
        }
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
        // O repositório aplica um limite de segurança de 100 itens por página.
        // Normalizar aqui evita que um chamador que solicite, por exemplo, 400
        // itens interprete a primeira página truncada como o lote completo.
        var effectivePageSize = Math.Clamp(pageSize, 1, 100);
        var allItems = new List<AssistedGradingItem>();
        var page = 1;
        while (true)
        {
            var pageItems = await repository.ListItemsByBatchAsync(batchId, page, effectivePageSize, cancellationToken);
            allItems.AddRange(pageItems);
            if (pageItems.Count < effectivePageSize)
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
