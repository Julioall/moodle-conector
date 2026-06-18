using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Domain.Grading;
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
    ICriteriaGenerationService criteriaGenerationService)
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

                var isSupported = artifact.ExtractionStatus == "succeeded";

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
                    artifact.ExtractionStatus == "succeeded" &&
                    !string.IsNullOrWhiteSpace(artifact.ExtractedTextRef))
                .ToArray();

            if (rubricArtifacts.Length > 0)
            {
                rubricDescription = Truncate(rubricArtifacts[0].ExtractedTextRef!, maxChars);
                maxGrade ??= ExtractMaxGrade(rubricDescription);
            }

            // 2. Selecionar o melhor artefato de contexto como enunciado da atividade.
            var contextArtifacts = artifacts
                .Where(artifact =>
                    artifact.ArtifactType == "assignment_context" &&
                    artifact.ExtractionStatus == "succeeded" &&
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
                    maxGrade ??= ExtractMaxGrade(selected.ExtractedText);
                    courseMaterials = $"{selected.Title}\n{selected.ExtractedText}";

                    // 3. Fallback: se nenhum critério estruturado foi extraído, usar o enunciado
                    // completo como critério aproximado — dá contexto suficiente para o serviço
                    // gerar feedback relevante mesmo sem rubrica formal.
                    if (string.IsNullOrWhiteSpace(criteria) && string.IsNullOrWhiteSpace(rubricDescription))
                    {
                        criteria = Truncate(selected.ExtractedText ?? string.Empty, maxChars);
                    }
                }
            }
        }

        if (maxGrade == null)
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
                    }
                }
                catch
                {
                    // Ignora falhas ao buscar nota máxima do Moodle, tenta continuar com o que foi extraído.
                }
            }
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

        var teacherInstructions = options.TeacherInstructions;
        if (!string.IsNullOrWhiteSpace(criteriaGenerationNotes))
        {
            teacherInstructions = string.IsNullOrWhiteSpace(teacherInstructions)
                ? criteriaGenerationNotes
                : $"{teacherInstructions}\n{criteriaGenerationNotes}";
        }

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
            teacherInstructions: teacherInstructions);
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

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[\.)]|[a-zA-Z][\.)])\s*")]
    private static partial Regex CriteriaPrefixRegex();
}
