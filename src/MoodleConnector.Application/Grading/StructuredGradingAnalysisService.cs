using MoodleConnector.Application.Abstractions;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Implementacao de analise assistida baseada em texto.
/// Esta versao gera um rascunho estruturado com base no texto da submissao e nos criterios informados.
/// Nunca bloqueia quando ha conteudo legivel — gera rascunho com confianca proporcional a qualidade dos criterios.
/// Para producao, substitua por implementacao que integre com servico de IA/LLM.
/// </summary>
public sealed partial class StructuredGradingAnalysisService : IGradingAnalysisService
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

        // --- Resolucao de criterios efetivos (cascata) ---
        var criteriaSource = ResolveCriteria(request.RubricOrCriteria, request.ActivityDescription, request.TeacherInstructions);
        var effectiveCriteria = criteriaSource.Text;
        var hasFormalCriteria = criteriaSource.Source == CriteriaSourceKind.Formal || criteriaSource.Source == CriteriaSourceKind.TeacherOverride;
        var hasApproximateCriteria = criteriaSource.Source == CriteriaSourceKind.Approximate;

        // --- Resolucao de MaxGrade efetivo (cascata) ---
        var effectiveMaxGrade = request.MaxGrade > 0
            ? request.MaxGrade
            : TryExtractMaxGrade(request.RubricOrCriteria) ?? TryExtractMaxGrade(request.ActivityDescription) ?? 0m;
        var hasMaxGrade = effectiveMaxGrade > 0;

        var wordCount = CountWords(request.SubmissionText);
        var submissionSnippet = request.SubmissionText.Length > 300
            ? request.SubmissionText[..300] + "..."
            : request.SubmissionText;

        // --- Sem criterios e sem descricao: rascunho generico com baixa confianca ---
        if (string.IsNullOrWhiteSpace(effectiveCriteria))
        {
            var lowConfNotes = new System.Text.StringBuilder();
            lowConfNotes.Append("Rascunho preliminar sem criterios ou descricao disponiveis. ");
            if (!hasMaxGrade)
            {
                lowConfNotes.Append("Escala de nota nao identificada. Nota sugerida indisponivel. ");
            }
            lowConfNotes.Append("Revisao manual obrigatoria. ");
            lowConfNotes.Append($"Trecho da submissao: {submissionSnippet}");

            return Task.FromResult(new GradingAnalysisResult(
                SuggestedGrade: null,
                Confidence: 0.15m,
                AnalysisStatus.Draft,
                FeedbackToStudent: BuildGenericFeedback(request.AssignmentName, wordCount),
                PrivateNotesToTeacher: lowConfNotes.ToString(),
                CriterionAnalysis: [],
                Blocks: []));
        }

        // --- Enunciado insuficiente: bloquear analise ---
        if (hasApproximateCriteria && CountWords(effectiveCriteria) < 15)
        {
            return Task.FromResult(new GradingAnalysisResult(
                SuggestedGrade: null,
                Confidence: 0m,
                AnalysisStatus.BlockedMissingCriteria,
                FeedbackToStudent: null,
                PrivateNotesToTeacher: "O texto extraido do enunciado da atividade e insuficiente para analise automatica (provavel imagem sem OCR ou apenas titulo de secao).",
                CriterionAnalysis: [],
                Blocks: ["Enunciado insuficiente ou sem criterios legiveis."]));
        }

        // --- Analise estruturada por criterio ---
        var criteria = hasApproximateCriteria 
            ? Array.Empty<string>() 
            : ParseCriteria(effectiveCriteria);

        if (criteria.Count == 0 && hasFormalCriteria)
        {
            // Criterios parseados resultaram vazios — gera rascunho generico
            return Task.FromResult(new GradingAnalysisResult(
                SuggestedGrade: null,
                Confidence: 0.2m,
                AnalysisStatus.Draft,
                FeedbackToStudent: BuildGenericFeedback(request.AssignmentName, wordCount),
                PrivateNotesToTeacher: $"Criterios extraidos do contexto nao geraram itens avaliáveis. Revisao manual obrigatoria. Trecho da submissao: {submissionSnippet}",
                CriterionAnalysis: [],
                Blocks: []));
        }

        var criterionResults = criteria.Count > 0
            ? (hasMaxGrade
                ? BuildCriterionAnalysis(criteria, request.SubmissionText, effectiveMaxGrade)
                : BuildCriterionAnalysisWithoutGrade(criteria, request.SubmissionText))
            : [];

        var totalSuggested = criterionResults.Count > 0 && hasMaxGrade 
            ? criterionResults.Sum(c => c.SuggestedPoints ?? 0) 
            : (decimal?)null;

        // Confianca base depende da fonte dos criterios
        var baseConfidence = hasFormalCriteria
            ? CalculateConfidence(wordCount, criterionResults)
            : hasApproximateCriteria
                ? Math.Min(CalculateConfidence(wordCount, criterionResults), 0.5m)
                : 0.3m;

        // Reduzir confianca ainda mais se nao houver MaxGrade
        if (!hasMaxGrade)
        {
            baseConfidence = Math.Min(baseConfidence, 0.35m);
        }

        var teacherNotes = BuildTeacherNotes(criterionResults, request.SubmissionText, baseConfidence,
            criteriaSource.Source, hasMaxGrade, effectiveMaxGrade);

        return Task.FromResult(new GradingAnalysisResult(
            SuggestedGrade: totalSuggested,
            Confidence: baseConfidence,
            AnalysisStatus.Draft,
            FeedbackToStudent: BuildStructuredFeedback(request.AssignmentName, criterionResults, wordCount),
            PrivateNotesToTeacher: teacherNotes,
            criterionResults,
            Blocks: []));
    }

    private static CriteriaResolution ResolveCriteria(string? rubricOrCriteria, string? activityDescription, string? teacherInstructions)
    {
        var hasTeacherInstructions = !string.IsNullOrWhiteSpace(teacherInstructions);
        var criteriaText = rubricOrCriteria;

        if (!string.IsNullOrWhiteSpace(criteriaText))
        {
            if (hasTeacherInstructions)
            {
                return new CriteriaResolution($"{criteriaText}\n{teacherInstructions}", CriteriaSourceKind.TeacherOverride);
            }
            return new CriteriaResolution(criteriaText, CriteriaSourceKind.Formal);
        }

        if (!string.IsNullOrWhiteSpace(activityDescription))
        {
            if (hasTeacherInstructions)
            {
                return new CriteriaResolution($"{activityDescription}\n{teacherInstructions}", CriteriaSourceKind.TeacherOverride);
            }
            return new CriteriaResolution(activityDescription, CriteriaSourceKind.Approximate);
        }

        if (hasTeacherInstructions)
        {
            return new CriteriaResolution(teacherInstructions, CriteriaSourceKind.TeacherOverride);
        }

        return new CriteriaResolution(null, CriteriaSourceKind.None);
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

    private static IReadOnlyList<GradingCriterionAnalysis> BuildCriterionAnalysisWithoutGrade(
        IReadOnlyList<string> criteria,
        string submissionText)
    {
        if (criteria.Count == 0)
        {
            return [];
        }

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

            var needsReview = coverage < 0.5m;

            results.Add(new GradingCriterionAnalysis(
                CriterionId: $"C{i + 1}",
                CriterionText: criterion,
                MaxPoints: null,
                SuggestedPoints: null,
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
        var cleanName = CleanAssignmentName(assignmentName);
        return $"Ola! Sua entrega para a atividade '{cleanName}' foi recebida com {wordCount} palavra(s). " +
               "Agradecemos pela participacao. Para detalhes sobre a avaliacao, aguarde o retorno do professor/tutor.";
    }

    private static string BuildStructuredFeedback(
        string assignmentName,
        IReadOnlyList<GradingCriterionAnalysis> criteria,
        int wordCount)
    {
        var sb = new System.Text.StringBuilder();
        var cleanName = CleanAssignmentName(assignmentName);
        sb.AppendLine($"Ola! Obrigado pela sua entrega da atividade '{cleanName}'.");
        sb.AppendLine();

        var strongCriteria = criteria.Where(c => (c.SuggestedPoints ?? 0) >= (c.MaxPoints ?? 0) * 0.7m).ToList();
        var weakCriteria = criteria.Where(c => c.TeacherReviewRequired).ToList();

        if (strongCriteria.Count > 0)
        {
            sb.AppendLine("**Pontos positivos identificados:**");
            foreach (var c in strongCriteria)
            {
                sb.AppendLine($"- {c.CriterionText}: evidenciou o aspecto esperado na resolucao.");
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

    private static string CleanAssignmentName(string name)
    {
        var cleaned = name;
        var prefixMatch = Regex.Match(cleaned, @"^(?:Envio\s*(?:da\s*)?atividade|Atividade)\s*[-–:]\s*", RegexOptions.IgnoreCase);
        if (prefixMatch.Success)
        {
            cleaned = cleaned[prefixMatch.Length..];
        }

        if (Regex.IsMatch(cleaned, @"^\d+$"))
        {
            return "Atividade"; // Fallback se so sobrou numero
        }

        return cleaned;
    }

    private static string BuildTeacherNotes(
        IReadOnlyList<GradingCriterionAnalysis> criteria,
        string submissionText,
        decimal confidence,
        CriteriaSourceKind criteriaSource,
        bool hasMaxGrade,
        decimal effectiveMaxGrade)
    {
        var reviews = criteria.Where(c => c.TeacherReviewRequired).ToList();
        var snippetLength = Math.Min(submissionText.Length, 500);
        var snippet = submissionText[..snippetLength];

        var notes = new System.Text.StringBuilder();
        notes.Append($"Analise estruturada gerada automaticamente. Confianca estimada: {confidence * 100:0}%. ");

        if (criteria.Count > 0)
        {
            notes.Append($"Criterios para revisao manual: {reviews.Count}/{criteria.Count}. ");
        }

        // Informar a origem dos criterios
        switch (criteriaSource)
        {
            case CriteriaSourceKind.TeacherOverride:
                notes.Append("Criterios modificados ou substituidos pelas instrucoes diretas do professor (TeacherInstructions usadas). ");
                break;
            case CriteriaSourceKind.Formal:
                notes.Append("Criterios extraidos de rubrica/criterios formais. ");
                break;
            case CriteriaSourceKind.Approximate:
                notes.Append("Enunciado da atividade selecionado como contexto. INSTRUCAO PARA A IA: O contexto selecionado nao possui rubrica formal. Use o texto extraido do enunciado como contexto pedagogico principal, e nao como lista direta de criterios. Primeiro interprete o enunciado, identifique comandos avaliativos e expectativas de resposta, ignorando cabecalhos, rodapes, logos, nomes, turma, enderecos e textos administrativos. Em seguida, compare a submissao do aluno com essas expectativas e gere feedback especifico, com pontos fortes e lacunas reais. So transforme em criterios avaliativos os comandos pedagogicos detectados ou derivados, nunca use as linhas brutas do documento como criterio. ");
                break;
            case CriteriaSourceKind.None:
                notes.Append("Nenhum criterio formal, instrucao ou descricao encontrados. Analise baseada apenas no conteudo da submissao. ");
                break;
        }

        if (!hasMaxGrade)
        {
            notes.Append("Escala de nota nao identificada. Nota sugerida indisponivel. ");
        }
        else if (effectiveMaxGrade > 0 && criteriaSource != CriteriaSourceKind.Formal)
        {
            notes.Append($"Valor da atividade extraido do contexto: {effectiveMaxGrade} pontos. Confirme se esta correto. ");
        }

        if (confidence < 0.5m)
        {
            notes.Append("Baixa confianca: revise manualmente a nota sugerida, pois a submissao tem pouca extensao textual ou baixa cobertura dos criterios. ");
        }

        notes.Append($"Trecho inicial da submissao: {snippet}");
        return notes.ToString();
    }

    private static decimal CalculateConfidence(
        int wordCount,
        IReadOnlyList<GradingCriterionAnalysis> criteria)
    {
        if (criteria.Count == 0)
        {
            return 0.3m;
        }

        var reviewRatio = (decimal)criteria.Count(c => c.TeacherReviewRequired) / criteria.Count;
        if (wordCount < 50 || reviewRatio >= 0.75m)
        {
            return 0.35m;
        }

        if (reviewRatio >= 0.5m)
        {
            return 0.5m;
        }

        return 0.7m;
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

    private static decimal? TryExtractMaxGrade(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = MaxGradeRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups["grade"].Value.Replace(',', '.');
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var grade) && grade > 0
            ? grade
            : null;
    }

    [GeneratedRegex(@"(?i)\b(?:valor(?:\s+da\s+atividade)?|nota\s*m[aá]xima|pontua[cç][aã]o|vale)\s*:?\s*(?<grade>\d+(?:[\.,]\d+)?)\s*(?:pontos?|pts?|%)?")]
    private static partial Regex MaxGradeRegex();

    private sealed record CriteriaResolution(string? Text, CriteriaSourceKind Source);

    private enum CriteriaSourceKind { None, Formal, Approximate, TeacherOverride }
}

