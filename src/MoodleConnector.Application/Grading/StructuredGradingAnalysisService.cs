using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Implementacao de analise assistida baseada em texto.
/// Esta versao gera um rascunho estruturado com base no texto da submissao e nos criterios informados.
/// Para producao, substitua por implementacao que integre com servico de IA/LLM.
/// </summary>
public sealed class StructuredGradingAnalysisService : IGradingAnalysisService
{
    public Task<GradingAnalysisResult> AnalyzeAsync(
        GradingAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SubmissionText))
        {
            return Task.FromResult(new GradingAnalysisResult(
                SuggestedGrade: null,
                Confidence: 0m,
                AnalysisStatus.BlockedEmptySubmission,
                FeedbackToStudent: null,
                PrivateNotesToTeacher: "Submissao sem texto legivel. Verifique se o arquivo foi baixado e extraido corretamente.",
                CriterionAnalysis: [],
                Blocks: ["Submissao sem conteudo textual para analise."]));
        }

        if (request.MaxGrade <= 0)
        {
            return Task.FromResult(new GradingAnalysisResult(
                SuggestedGrade: null,
                Confidence: 0m,
                AnalysisStatus.BlockedUnknownScale,
                FeedbackToStudent: null,
                PrivateNotesToTeacher: "Escala de nota invalida ou nao configurada para esta atividade.",
                CriterionAnalysis: [],
                Blocks: ["Escala de nota nao identificada. Nao e possivel sugerir nota sem referencia de valor maximo."]));
        }

        var hasCriteria = !string.IsNullOrWhiteSpace(request.RubricOrCriteria);
        var wordCount = CountWords(request.SubmissionText);
        var submissionSnippet = request.SubmissionText.Length > 300
            ? request.SubmissionText[..300] + "..."
            : request.SubmissionText;

        if (!hasCriteria)
        {
            return Task.FromResult(new GradingAnalysisResult(
                SuggestedGrade: null,
                Confidence: 0m,
                AnalysisStatus.BlockedMissingCriteria,
                FeedbackToStudent: BuildGenericFeedback(request.AssignmentName, wordCount),
                PrivateNotesToTeacher: $"Atividade sem rubrica ou criterios definidos. Revisao manual obrigatoria. Trecho da submissao: {submissionSnippet}",
                CriterionAnalysis: [],
                Blocks: ["Nao foram informados criterios ou rubrica para esta atividade. Nota sugerida indisponivel."]));
        }

        // Analise estruturada por criterio
        var criteria = ParseCriteria(request.RubricOrCriteria!);
        var criterionResults = BuildCriterionAnalysis(criteria, request.SubmissionText, request.MaxGrade);
        var totalSuggested = criterionResults.Sum(c => c.SuggestedPoints ?? 0);
        var confidence = hasCriteria && wordCount >= 50 ? 0.6m : 0.3m;

        return Task.FromResult(new GradingAnalysisResult(
            SuggestedGrade: totalSuggested,
            Confidence: confidence,
            AnalysisStatus.Draft,
            FeedbackToStudent: BuildStructuredFeedback(request.AssignmentName, criterionResults, wordCount),
            PrivateNotesToTeacher: BuildTeacherNotes(criterionResults, request.SubmissionText, confidence),
            criterionResults,
            Blocks: []));
    }

    private static IReadOnlyList<GradingCriterionAnalysis> BuildCriterionAnalysis(
        IReadOnlyList<string> criteria,
        string submissionText,
        decimal maxGrade)
    {
        if (criteria.Count == 0)
        {
            return [];
        }

        var pointsPerCriterion = Math.Round(maxGrade / criteria.Count, 2);
        var results = new List<GradingCriterionAnalysis>();
        var submissionLower = submissionText.ToLowerInvariant();

        for (var i = 0; i < criteria.Count; i++)
        {
            var criterion = criteria[i].Trim();
            var criterionWords = criterion.ToLowerInvariant()
                .Split([' ', ',', ';', '.', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length > 3)
                .Take(5)
                .ToArray();

            var matchedWords = criterionWords.Count(word => submissionLower.Contains(word));
            var coverage = criterionWords.Length > 0
                ? (decimal)matchedWords / criterionWords.Length
                : 0m;

            var suggestedPoints = Math.Round(pointsPerCriterion * Math.Min(coverage * 1.5m, 1m), 2);
            var needsReview = coverage < 0.5m;

            results.Add(new GradingCriterionAnalysis(
                CriterionId: $"C{i + 1}",
                CriterionText: criterion,
                MaxPoints: pointsPerCriterion,
                SuggestedPoints: suggestedPoints,
                EvidenceFound: matchedWords > 0
                    ? $"O estudante abordou {matchedWords} de {criterionWords.Length} aspecto(s) identificado(s) no criterio."
                    : "Nao foram encontradas evidencias diretamente relacionadas ao criterio no texto analisado.",
                Gaps: needsReview
                    ? $"O texto apresenta cobertura parcial ({Math.Round(coverage * 100)}%) dos aspectos esperados. Revisao recomendada."
                    : null,
                TeacherReviewRequired: needsReview));
        }

        return results;
    }

    private static string BuildGenericFeedback(string assignmentName, int wordCount)
    {
        return $"Ola! Sua entrega para a atividade '{assignmentName}' foi recebida com {wordCount} palavra(s). " +
               "Agradecemos pela participacao. Para detalhes sobre a avaliacao, aguarde o retorno do professor/tutor.";
    }

    private static string BuildStructuredFeedback(
        string assignmentName,
        IReadOnlyList<GradingCriterionAnalysis> criteria,
        int wordCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Ola! Obrigado pela sua entrega da atividade '{assignmentName}'.");
        sb.AppendLine();

        var strongCriteria = criteria.Where(c => (c.SuggestedPoints ?? 0) >= (c.MaxPoints ?? 0) * 0.7m).ToList();
        var weakCriteria = criteria.Where(c => c.TeacherReviewRequired).ToList();

        if (strongCriteria.Count > 0)
        {
            sb.AppendLine("**Pontos fortes identificados:**");
            foreach (var c in strongCriteria)
            {
                sb.AppendLine($"- {c.CriterionText}: boa cobertura evidenciada no texto.");
            }
            sb.AppendLine();
        }

        if (weakCriteria.Count > 0)
        {
            sb.AppendLine("**Aspectos para desenvolvimento:**");
            foreach (var c in weakCriteria)
            {
                sb.AppendLine($"- {c.CriterionText}: {c.Gaps}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Este e um parecer preliminar assistido. A avaliacao final sera feita pelo professor/tutor.");
        return sb.ToString().Trim();
    }

    private static string BuildTeacherNotes(
        IReadOnlyList<GradingCriterionAnalysis> criteria,
        string submissionText,
        decimal confidence)
    {
        var reviews = criteria.Where(c => c.TeacherReviewRequired).ToList();
        var snippetLength = Math.Min(submissionText.Length, 500);
        var snippet = submissionText[..snippetLength];

        return $"Analise estruturada gerada automaticamente. Confianca estimada: {confidence * 100:0}%. " +
               $"Criterios para revisao manual: {reviews.Count}/{criteria.Count}. " +
               $"Trecho inicial da submissao: {snippet}";
    }

    private static IReadOnlyList<string> ParseCriteria(string rubricOrCriteria)
    {
        return rubricOrCriteria
            .Split(['\n', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.TrimStart('-', '*', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' ').Trim())
            .Where(c => c.Length > 3)
            .Take(20)
            .ToArray();
    }

    private static int CountWords(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
