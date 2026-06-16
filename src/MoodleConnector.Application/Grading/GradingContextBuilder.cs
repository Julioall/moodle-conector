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
    IAssignmentContextSelectionService contextSelectionService)
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
                        criteria = Truncate(selected.ExtractedText, maxChars);
                    }
                }
            }
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
            teacherInstructions: options.TeacherInstructions);
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

    [GeneratedRegex(@"(?i)\b(?:valor(?:\s+da\s+atividade)?|nota\s*m[aá]xima|pontua[cç][aã]o|vale)\s*:?\s*(?<grade>\d+(?:[\.,]\d+)?)\s*(?:pontos?|pts?|%)?")]
    private static partial Regex MaxGradeRegex();

    [GeneratedRegex(@"(?i)\b(?:criterios?|crit[eé]rios?|rubrica|avaliacao|avalia[cç][aã]o)\b")]
    private static partial Regex CriteriaHeaderRegex();

    [GeneratedRegex(@"(?i)\b(?:entrega|prazo|observa[cç][oõ]es?|formato|refer[eê]ncias?|produto\s+esperado)\b")]
    private static partial Regex StopCriteriaRegex();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[\.)]|[a-zA-Z][\.)])\s*")]
    private static partial Regex CriteriaPrefixRegex();
}
