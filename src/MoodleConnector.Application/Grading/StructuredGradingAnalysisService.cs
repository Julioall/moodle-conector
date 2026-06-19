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
        // Quando temos criterios Approximate E MaxGrade disponivel, permitir
        // parsing dos criterios para gerar nota estimada (cenario SAP/PDF).
        var criteria = hasApproximateCriteria
            ? (hasMaxGrade ? ParseCriteria(effectiveCriteria) : Array.Empty<string>())
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

        decimal? totalSuggested;
        if (criterionResults.Count > 0 && hasMaxGrade)
        {
            totalSuggested = criterionResults.Sum(c => c.SuggestedPoints ?? 0);
        }
        else if (hasApproximateCriteria && hasMaxGrade && !string.IsNullOrWhiteSpace(effectiveCriteria))
        {
            // Cenário Approximate sem critérios parseados mas com MaxGrade:
            // gerar nota proporcional baseada em cobertura de keywords do enunciado.
            totalSuggested = EstimateGradeFromCoverage(effectiveCriteria, request.SubmissionText, effectiveMaxGrade);
        }
        else
        {
            totalSuggested = null;
        }

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

        var results = new List<GradingCriterionAnalysis>();
        var submissionLower = submissionText.ToLowerInvariant();

        // Distribuir pontos com residual no ultimo criterio
        var pointsPerCriterion = Math.Round(maxGrade / criteria.Count, 2);
        var accumulatedPoints = 0m;

        for (var i = 0; i < criteria.Count; i++)
        {
            var criterion = criteria[i].Trim();
            var criterionWords = ExtractSignificantWords(criterion);

            var matchedWords = criterionWords.Where(word => submissionLower.Contains(word)).ToArray();
            var coverage = criterionWords.Length > 0
                ? (decimal)matchedWords.Length / criterionWords.Length
                : 0m;

            // Ultimo criterio recebe o residual
            var currentMax = (i == criteria.Count - 1 && maxGrade > 0)
                ? maxGrade - accumulatedPoints
                : pointsPerCriterion;
            accumulatedPoints += currentMax;

            var suggestedPoints = Math.Round(currentMax * Math.Min(coverage * 1.5m, 1m), 2);
            var needsReview = coverage < 0.5m;

            // Evidence com palavras-chave concretas da entrega
            var evidence = matchedWords.Length > 0
                ? BuildConcreteEvidence(criterion, matchedWords)
                : "Nao foram identificados elementos diretamente relacionados a este aspecto no texto analisado.";

            // Gaps com orientacao especifica
            var gaps = needsReview
                ? BuildConcreteGaps(criterion, criterionWords, matchedWords)
                : null;

            results.Add(new GradingCriterionAnalysis(
                CriterionId: $"C{i + 1}",
                CriterionText: criterion,
                MaxPoints: currentMax,
                SuggestedPoints: suggestedPoints,
                EvidenceFound: evidence,
                Gaps: gaps,
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
            var criterionWords = ExtractSignificantWords(criterion);

            var matchedWords = criterionWords.Where(word => submissionLower.Contains(word)).ToArray();
            var coverage = criterionWords.Length > 0
                ? (decimal)matchedWords.Length / criterionWords.Length
                : 0m;

            var needsReview = coverage < 0.5m;

            results.Add(new GradingCriterionAnalysis(
                CriterionId: $"C{i + 1}",
                CriterionText: criterion,
                MaxPoints: null,
                SuggestedPoints: null,
                EvidenceFound: matchedWords.Length > 0
                    ? BuildConcreteEvidence(criterion, matchedWords)
                    : "Nao foram identificados elementos diretamente relacionados a este aspecto no texto analisado.",
                Gaps: needsReview
                    ? BuildConcreteGaps(criterion, criterionWords, matchedWords)
                    : null,
                TeacherReviewRequired: needsReview));
        }

        return results;
    }

    private static string BuildGenericFeedback(string assignmentName, int wordCount)
    {
        var cleanName = CleanAssignmentName(assignmentName);
        return $"O trabalho para a atividade '{cleanName}' foi recebido. " +
               "Aguarde o retorno do professor/tutor com a avaliacao detalhada.";
    }

    /// <summary>
    /// Gera feedback natural em paragrafos, com evidencias reais da entrega.
    /// Formato: abertura positiva + pontos fortes concretos + melhorias objetivas.
    /// Pronto para copiar no Moodle — sem listas mecanicas nem frases genericas.
    /// </summary>
    private static string BuildStructuredFeedback(
        string assignmentName,
        IReadOnlyList<GradingCriterionAnalysis> criteria,
        int wordCount)
    {
        var sb = new System.Text.StringBuilder();

        var strongCriteria = criteria.Where(c => (c.SuggestedPoints ?? 0) >= (c.MaxPoints ?? 0) * 0.5m).ToList();
        var weakCriteria = criteria.Where(c => c.TeacherReviewRequired).ToList();

        // --- Abertura positiva ---
        if (strongCriteria.Count >= criteria.Count / 2)
        {
            sb.Append("Bom trabalho. ");
        }
        else
        {
            sb.Append("O trabalho apresenta esforco na resolucao da atividade. ");
        }

        // --- Pontos fortes: texto corrido com elementos reais ---
        if (strongCriteria.Count > 0)
        {
            var strongTexts = strongCriteria
                .Select(c => SummarizeCriterionPositive(c))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (strongTexts.Count > 0)
            {
                sb.Append(JoinNaturalList(strongTexts));
                sb.Append(". ");
            }
        }

        // --- Melhorias: texto corrido com orientacoes concretas ---
        if (weakCriteria.Count > 0)
        {
            sb.Append("Para melhorar, ");
            var improvementTexts = weakCriteria
                .Select(c => SummarizeCriterionImprovement(c))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (improvementTexts.Count > 0)
            {
                sb.Append(JoinNaturalList(improvementTexts));
                sb.Append('.');
            }
            else
            {
                sb.Append("revise os aspectos que precisam de maior aprofundamento.");
            }
        }
        else if (strongCriteria.Count < criteria.Count)
        {
            sb.Append("Considere revisar os pontos que podem ser aprofundados.");
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Gera resumo positivo de um criterio atendido, usando as evidencias encontradas.
    /// </summary>
    private static string SummarizeCriterionPositive(GradingCriterionAnalysis criterion)
    {
        // Usar a evidencia concreta se disponivel
        if (!string.IsNullOrWhiteSpace(criterion.EvidenceFound) &&
            !criterion.EvidenceFound.StartsWith("Nao foram", StringComparison.OrdinalIgnoreCase))
        {
            return LowercaseFirst(criterion.EvidenceFound.TrimEnd('.'));
        }

        return $"demonstrou compreensao do aspecto '{LowercaseFirst(criterion.CriterionText)}'";
    }

    /// <summary>
    /// Gera sugestao de melhoria a partir das lacunas identificadas.
    /// </summary>
    private static string SummarizeCriterionImprovement(GradingCriterionAnalysis criterion)
    {
        if (!string.IsNullOrWhiteSpace(criterion.Gaps))
        {
            return LowercaseFirst(criterion.Gaps.TrimEnd('.'));
        }

        return $"aprofunde o aspecto '{LowercaseFirst(criterion.CriterionText)}'";
    }

    /// <summary>
    /// Junta itens em lista natural com virgulas e 'e' antes do ultimo.
    /// </summary>
    private static string JoinNaturalList(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => UppercaseFirst(items[0]),
            2 => $"{UppercaseFirst(items[0])} e {items[1]}",
            _ => $"{UppercaseFirst(string.Join(", ", items.Take(items.Count - 1)))} e {items[^1]}"
        };
    }

    private static string LowercaseFirst(string text)
    {
        return string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static string UppercaseFirst(string text)
    {
        return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
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

    /// <summary>
    /// Parseia criterios de uma string, rejeitando fragmentos inuteis.
    /// Requer pelo menos 3 palavras uteis (>3 chars) por criterio.
    /// Limita a 6 criterios para manter qualidade.
    /// </summary>
    private static IReadOnlyList<string> ParseCriteria(string rubricOrCriteria)
    {
        return rubricOrCriteria
            .Split(['\n', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.TrimStart('-', '*', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' ').Trim())
            .Where(c => CountUsefulWords(c) >= 3)
            .Take(6)
            .ToArray();
    }

    /// <summary>
    /// Conta palavras uteis (>3 chars) em um texto.
    /// </summary>
    private static int CountUsefulWords(string text)
    {
        return text.Split([' ', ',', ';', '.', ':'], StringSplitOptions.RemoveEmptyEntries)
            .Count(word => word.Length > 3);
    }

    /// <summary>
    /// Extrai palavras significativas de um criterio para matching.
    /// Usa palavras >3 chars, ate 8 por criterio.
    /// </summary>
    private static string[] ExtractSignificantWords(string criterion)
    {
        return criterion.ToLowerInvariant()
            .Split([' ', ',', ';', '.', ':', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 3)
            .Where(word => !IsStopWord(word))
            .Take(8)
            .ToArray();
    }

    private static bool IsStopWord(string word)
    {
        return word is "para" or "como" or "pela" or "pelo" or "pelas" or "pelos"
            or "mais" or "cada" or "este" or "esta" or "esse" or "essa"
            or "entre" or "sobre" or "tambem" or "também" or "deve" or "devem"
            or "sendo" or "foram" or "será" or "sera" or "quando" or "onde"
            or "toda" or "todo" or "todas" or "todos" or "seus" or "suas";
    }

    /// <summary>
    /// Constroi evidencia concreta com palavras-chave encontradas na entrega.
    /// </summary>
    private static string BuildConcreteEvidence(string criterion, string[] matchedWords)
    {
        if (matchedWords.Length == 0)
        {
            return "Nao foram identificados elementos diretamente relacionados a este aspecto no texto analisado.";
        }

        var keywordsDisplay = matchedWords.Length <= 4
            ? string.Join(", ", matchedWords)
            : string.Join(", ", matchedWords.Take(4)) + " e outros";

        return $"O texto aborda elementos relacionados ({keywordsDisplay}), demonstrando compreensao do aspecto solicitado";
    }

    /// <summary>
    /// Constroi lacunas com orientacao especifica sobre o que melhorar.
    /// </summary>
    private static string BuildConcreteGaps(string criterion, string[] criterionWords, string[] matchedWords)
    {
        var missingWords = criterionWords.Except(matchedWords).Take(3).ToArray();
        if (missingWords.Length > 0)
        {
            var missing = string.Join(", ", missingWords);
            return $"Aprofunde os aspectos relacionados a {missing} para atender melhor ao criterio";
        }

        return $"Aprofunde a abordagem sobre '{LowercaseFirst(criterion)}' com mais detalhamento";
    }

    /// <summary>
    /// Estima nota baseada na cobertura de palavras-chave do enunciado
    /// na submissão, para o cenário Approximate sem critérios parseados.
    /// </summary>
    private static decimal EstimateGradeFromCoverage(string contextText, string submissionText, decimal maxGrade)
    {
        var contextWords = contextText.ToLowerInvariant()
            .Split([' ', ',', ';', '.', ':', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 4) // Só palavras significativas
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToArray();

        if (contextWords.Length == 0)
        {
            return 0m;
        }

        var submissionLower = submissionText.ToLowerInvariant();
        var matchedWords = contextWords.Count(w => submissionLower.Contains(w));
        var coverage = (decimal)matchedWords / contextWords.Length;

        // Escala conservadora: coverage 100% → 70% da nota máxima (pois é aproximado)
        var estimatedGrade = Math.Round(maxGrade * Math.Min(coverage * 0.7m, 0.7m), 2);
        return Math.Max(0m, estimatedGrade);
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

