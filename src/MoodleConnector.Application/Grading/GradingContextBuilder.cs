using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Domain.Grading;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Implementação MVP do construtor de contexto de correção.
/// Reutiliza artefatos já extraídos salvos no repositório.
/// Download e extração de novos arquivos ficam para fase futura (sem Moodle real).
/// </summary>
public sealed class GradingContextBuilder(
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
                    courseMaterials = $"{selected.Title}\n{selected.ExtractedText}";
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
            criteria: null,
            rubricDescription: null,
            maxGrade: null,
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
}

