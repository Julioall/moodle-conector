using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Domain.Grading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Implementação MVP do construtor de contexto de correção.
/// Reutiliza artefatos já extraídos salvos no repositório.
/// Download e extração de novos arquivos ficam para fase futura (sem Moodle real).
/// </summary>
public sealed partial class GradingContextBuilder(
    IGradingReviewRepository repository,
    IOptions<GradingLimitsOptions> limits,
    IAssignmentContextSelectionService contextSelectionService,
    IMoodleAssignmentSettingsGateway settingsGateway,
    ICriteriaGenerationService criteriaGenerationService,
    ILogger<GradingContextBuilder>? logger = null)
    : IGradingContextBuilder
{
    public async Task<GradingContext> BuildAsync(
        AssistedGradingItem item,
        GradingContextOptions options,
        CancellationToken cancellationToken)
    {
        var maxFiles = limits.Value.MaxFilesPerSubmission;
        var maxChars = limits.Value.MaxTextCharsPerSubmission;

        string? submissionText = null;
        string? assignmentStatement = null;
        string? criteria = null;
        string? rubricDescription = null;
        decimal? maxGrade = null;
        string? courseMaterials = null;
        string? criteriaGenerationNotes = null;
        var attachedFiles = new List<GradingFileInfo>();
        IReadOnlyList<GradingArtifact> artifacts = [];

        // ============================================================
        // PASSO 1: Buscar MaxGrade PRIMEIRO via API Moodle.
        // A API mod_assign_get_assignments retorna o campo 'grade'
        // configurado pelo professor na atividade — é a fonte autoritativa.
        // Deve ser chamada ANTES de qualquer regex/heurística para evitar
        // que 'Valor: 16 pontos' do PDF sobrescreva os 49 reais da API.
        // ============================================================
        {
            var batch = await repository.GetBatchAsync(item.BatchId, cancellationToken);
            if (batch != null)
            {
                try
                {
                    var settings = await settingsGateway.GetAssignmentSettingsAsync(
                        batch.CreatedBySubject,
                        item.CourseId.ToString(CultureInfo.InvariantCulture),
                        item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                        cancellationToken);

                    if (settings != null && settings.MaxGrade > 0)
                    {
                        maxGrade = settings.MaxGrade;
                        logger?.LogInformation(
                            "MaxGrade obtida via API Moodle (fonte autoritativa): {MaxGrade} para assignment {AssignmentId}",
                            maxGrade, item.AssignmentId);
                    }
                    else if (settings != null)
                    {
                        logger?.LogWarning(
                            "API Moodle retornou MaxGrade={MaxGrade} para assignment {AssignmentId}. Pode ser escala (negativo) ou nao configurada.",
                            settings.MaxGrade, item.AssignmentId);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(
                        ex,
                        "Falha ao buscar MaxGrade via API Moodle para assignment {AssignmentId} do curso {CourseId}. Tentando fallback por rubrica/regex.",
                        item.AssignmentId,
                        item.CourseId);
                }
            }
        }

        if (options.IncludeSubmissionFiles || options.IncludeCourseMaterials)
        {
            artifacts = await repository.ListArtifactsByItemAsync(item.Id, cancellationToken);
        }

        if (options.IncludeSubmissionFiles)
        {
            var fileArtifacts = artifacts
                .Where(artifact => artifact.ArtifactType == "submission_file")
                .Take(maxFiles)
                .ToArray();

            foreach (var artifact in fileArtifacts)
            {
                var extracted = !string.IsNullOrWhiteSpace(artifact.ExtractedTextRef)
                    ? Truncate(artifact.ExtractedTextRef, maxChars)
                    : null;

                var isSupported = artifact.ExtractionStatus is "succeeded" or "ocr_extracted";

                attachedFiles.Add(new GradingFileInfo(
                    artifact.Filename ?? "unknown",
                    artifact.MimeType,
                    artifact.SizeBytes,
                    artifact.Sha256,
                    extracted,
                    isSupported));

                if (extracted != null && submissionText == null)
                {
                    submissionText = extracted;
                }
            }
        }

        if (options.IncludeCourseMaterials)
        {
            // 1. Tentar rubrica formal (artefatos do tipo 'rubric').
            var rubricArtifacts = artifacts
                .Where(artifact =>
                    artifact.ArtifactType == "rubric" &&
                    artifact.ExtractionStatus is "succeeded" or "ocr_extracted" &&
                    !string.IsNullOrWhiteSpace(artifact.ExtractedTextRef))
                .ToArray();

            if (rubricArtifacts.Length > 0)
            {
                rubricDescription = Truncate(rubricArtifacts[0].ExtractedTextRef!, maxChars);
                // Só usar regex na rubrica se a API Moodle não retornou MaxGrade
                if (maxGrade == null)
                {
                    maxGrade = ExtractMaxGrade(rubricDescription);
                    if (maxGrade != null)
                    {
                        logger?.LogDebug(
                            "MaxGrade extraida via regex de rubrica: {MaxGrade} para assignment {AssignmentId}",
                            maxGrade, item.AssignmentId);
                    }
                }
            }

            // 2. Selecionar o melhor artefato de contexto como enunciado da atividade.
            var contextArtifacts = artifacts
                .Where(artifact =>
                    artifact.ArtifactType == "assignment_context" &&
                    artifact.ExtractionStatus is "succeeded" or "ocr_extracted" &&
                    !string.IsNullOrWhiteSpace(artifact.ExtractedTextRef))
                .ToArray();

            if (contextArtifacts.Length > 0)
            {
                var candidates = contextArtifacts
                    .Select((artifact, index) => new AssignmentContextCandidate(
                        artifact.Id.ToString(),
                        artifact.ArtifactType,
                        artifact.Filename ?? $"context-{index + 1}",
                        Truncate(artifact.ExtractedTextRef!, maxChars),
                        SectionNumber: null,
                        DistanceFromAssignment: index))
                    .ToArray();
                var selection = await contextSelectionService.SelectAsync(
                    new AssignmentContextSelectionRequest(
                        item.CourseId.ToString(CultureInfo.InvariantCulture),
                        item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                        $"Tarefa {item.AssignmentId.ToString(CultureInfo.InvariantCulture)}",
                        AssignmentDescription: null,
                        candidates),
                    cancellationToken);

                var selected = candidates.FirstOrDefault(candidate =>
                    candidate.CandidateId == selection.SelectedCandidateId);
                if (selected is not null)
                {
                    assignmentStatement = selected.ExtractedText;
                    criteria = ExtractCriteria(selected.ExtractedText);
                    courseMaterials = $"{selected.Title}\n{selected.ExtractedText}";

                    // NÃO usar texto bruto do enunciado como critério direto.
                    // Quando ExtractCriteria retorna null e não há rubrica, o campo criteria
                    // fica null - o HeuristicCriteriaGenerationService abaixo tenta gerar
                    // critérios semânticos, e o StructuredGradingAnalysisService usa o
                    // assignmentStatement como contexto pedagógico (não como critérios).
                }
            }
        }

        // Fallback regex: tentar extrair nota máxima do texto do enunciado
        // SOMENTE quando a API Moodle e a rubrica não retornaram MaxGrade.
        if (maxGrade == null && !string.IsNullOrWhiteSpace(assignmentStatement))
        {
            maxGrade = ExtractMaxGrade(assignmentStatement);
            if (maxGrade != null)
            {
                logger?.LogDebug(
                    "MaxGrade extraida via regex do enunciado: {MaxGrade} para assignment {AssignmentId}",
                    maxGrade, item.AssignmentId);
            }
        }

        // Fallback final: usar padrão Moodle (100 pontos) quando todas as fontes falham.
        // O Moodle v5 cria atividades com grade=100 por padrão. Melhor estimar com 100
        // e reduzir confiança do que não gerar nota nenhuma.
        if (maxGrade == null || maxGrade == 0m)
        {
            maxGrade = 100m;
            logger?.LogInformation(
                "MaxGrade nao identificada para assignment {AssignmentId}. Usando padrao Moodle (100 pontos).",
                item.AssignmentId);
        }

        // 4. Validação de qualidade dos critérios e fallback via geração estruturada.
        // Se os critérios extraídos por heurística são de baixa qualidade (contaminados,
        // truncados, formados apenas por metadados), tentar gerar critérios limpos.
        if (string.IsNullOrWhiteSpace(rubricDescription) &&
            AreCriteriaLowQuality(criteria))
        {
            try
            {
                var genResult = await criteriaGenerationService.GenerateAsync(
                    new CriteriaGenerationRequest(
                        AssignmentName: $"Tarefa {item.AssignmentId.ToString(CultureInfo.InvariantCulture)}",
                        AssignmentDescription: assignmentStatement,
                        ContextText: courseMaterials,
                        SupportingMaterials: null,
                        MaxGrade: maxGrade ?? 0m),
                    cancellationToken);

                if (genResult.Criteria.Count > 0)
                {
                    // Converter critérios estruturados para o formato string esperado pelo GradingContext
                    criteria = string.Join('\n', genResult.Criteria.Select(c => c.Description));
                    criteriaGenerationNotes = genResult.PrivateNotesToTeacher;
                }
            }
            catch
            {
                // Se a geração falhar, mantém os critérios heurísticos originais.
            }
        }

        // TeacherInstructions recebe SOMENTE instruções reais do professor (options).
        // CriteriaGenerationNotes vai como campo separado no GradingContext para
        // não contaminar a resolução de critérios no StructuredGradingAnalysisService.
        var teacherInstructions = options.TeacherInstructions;

        return GradingContext.Build(
            gradingItemId: item.Id,
            batchId: item.BatchId,
            courseId: item.CourseId.ToString(CultureInfo.InvariantCulture),
            assignmentId: item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            submissionId: item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
            studentId: item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            assignmentStatement: assignmentStatement,
            criteria: criteria,
            rubricDescription: rubricDescription,
            maxGrade: maxGrade,
            gradeScale: null,
            submissionText: submissionText,
            attachedFiles: attachedFiles,
            courseMaterials: courseMaterials,
            teacherInstructions: teacherInstructions,
            criteriaGenerationNotes: criteriaGenerationNotes);
    }

    private static string Truncate(string text, int maxChars)
    {
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static decimal? ExtractMaxGrade(string? text)
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

    private static string? ExtractCriteria(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var criteria = new List<string>();
        var collecting = false;

        foreach (var line in lines)
        {
            if (CriteriaHeaderRegex().IsMatch(line))
            {
                collecting = true;
                AddInlineCriteriaFromHeader(line, criteria);
                continue;
            }

            if (!collecting)
            {
                continue;
            }

            if (criteria.Count > 0 && StopCriteriaRegex().IsMatch(line))
            {
                break;
            }

            var cleaned = CriteriaPrefixRegex().Replace(line, string.Empty).Trim();
            if (cleaned.Length >= 8)
            {
                criteria.Add(cleaned);
            }
        }

        return criteria.Count == 0 ? null : string.Join('\n', criteria.Take(20));
    }

    private static void AddInlineCriteriaFromHeader(string line, List<string> criteria)
    {
        var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == line.Length - 1)
        {
            return;
        }

        var inlineCriteria = line[(separatorIndex + 1)..]
            .Split([';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => CriteriaPrefixRegex().Replace(item, string.Empty).Trim().TrimEnd('.'))
            .Where(item => item.Length >= 8);

        criteria.AddRange(inlineCriteria);
    }

    /// <summary>
    /// Verifica se os critérios extraídos por heurística são de baixa qualidade.
    /// Retorna true quando os critérios estão vazios, contaminados por metadados,
    /// truncados ou genéricos demais para serem usados na correção assistida.
    /// </summary>
    internal static bool AreCriteriaLowQuality(string? criteria)
    {
        if (string.IsNullOrWhiteSpace(criteria))
        {
            return true;
        }

        // Se o texto contém marcadores de estrutura de documento (SAP/enunciado bruto),
        // significa que o texto completo do documento foi usado como fallback de critérios
        // em vez de critérios estruturados extraídos. Nesse caso, o serviço de geração
        // pode produzir critérios muito melhores a partir do mesmo texto.
        var lower = criteria.ToLowerInvariant();
        var documentMarkers = new[]
        {
            "resultados esperados", "produto esperado",
            "situação de aprendizagem", "situacao de aprendizagem",
            "envio sap"
        };
        if (documentMarkers.Any(marker => lower.Contains(marker)))
        {
            return true;
        }

        var lines = criteria
            .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            return true;
        }

        var lowQualityCount = 0;
        foreach (var line in lines)
        {
            if (IsLowQualityCriterion(line))
            {
                lowQualityCount++;
            }
        }

        // Se mais de 50% dos critérios são de baixa qualidade, acionar fallback
        return (double)lowQualityCount / lines.Length > 0.5;
    }

    private static bool IsLowQualityCriterion(string criterion)
    {
        var lower = criterion.ToLowerInvariant();

        // Critério muito curto
        if (criterion.Length < 15)
        {
            return true;
        }

        // Contém metadados bloqueados sem conteúdo avaliável
        var metadataTokens = new[]
        {
            "à distância", "a distancia", "individual", "momento",
            "carga horária", "carga horaria", "cabeçalho", "cabecalho",
            "modalidade", "nível", "nivel"
        };
        var hasMetadata = metadataTokens.Any(m => lower.Contains(m));
        var evaluableVerbs = new[]
        {
            "elaborar", "indicar", "apresentar", "descrever", "comparar",
            "justificar", "aplicar", "adequar", "identificar", "analisar",
            "relacionar", "propor", "planejar", "demonstrar", "avaliar",
            "explicar", "classificar", "definir", "organizar", "formular"
        };
        var hasVerb = evaluableVerbs.Any(v => lower.Contains(v));

        // Se tem metadado mas não tem verbo avaliável, é baixa qualidade
        if (hasMetadata && !hasVerb)
        {
            return true;
        }

        // Se é um bloco grande de texto genérico sem marcadores avaliáveis
        if (criterion.Length > 200 && !hasVerb)
        {
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"(?i)\b(?:valor(?:\s+da\s+atividade)?|nota\s*m[aá]xima|pontua[cç][aã]o|vale)\s*:?\s*(?<grade>\d+(?:[\.,]\d+)?)\s*(?:pontos?|pts?|%)?")]
    private static partial Regex MaxGradeRegex();

    [GeneratedRegex(@"(?i)\b(?:criterios?|crit[eé]rios?|rubrica|avaliacao|avalia[cç][aã]o)\b")]
    private static partial Regex CriteriaHeaderRegex();

    [GeneratedRegex(@"(?i)\b(?:entrega|prazo|observa[cç][oõ]es?|formato|refer[eê]ncias?|produto\s+esperado)\b")]
    private static partial Regex StopCriteriaRegex();

    [GeneratedRegex(@"^\s*(?:[-*.]|\d+[\.)]|[a-zA-Z][\.)])\s*")]
    private static partial Regex CriteriaPrefixRegex();
}
