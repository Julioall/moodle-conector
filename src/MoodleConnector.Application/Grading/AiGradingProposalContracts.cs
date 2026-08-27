using System.Text.Json.Serialization;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Entrada estruturada opcional para substituir o contrato legado de nota/feedback.
/// A escala numérica continua sendo resolvida exclusivamente a partir do Moodle.
/// </summary>
public sealed record AiGradingProposalInput(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("contextHash")] string? ContextHash,
    [property: JsonPropertyName("suggestedGrade")] decimal? SuggestedGrade,
    [property: JsonPropertyName("feedback")] string? Feedback,
    [property: JsonPropertyName("criteria")] IReadOnlyList<AiGradingCriterionInput>? Criteria,
    [property: JsonPropertyName("evidence")] IReadOnlyList<AiGradingEvidenceInput>? Evidence,
    [property: JsonPropertyName("gaps")] IReadOnlyList<string>? Gaps,
    [property: JsonPropertyName("modelConfidence")] decimal? ModelConfidence,
    [property: JsonPropertyName("extraction")] GradingExtractionSummary? Extraction,
    [property: JsonPropertyName("coverage")] GradingEvidenceCoverage? Coverage,
    [property: JsonPropertyName("status")] string? Status = null);

public sealed record AiGradingCriterionInput(
    [property: JsonPropertyName("criterionId")] string CriterionId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("maxPoints")] decimal? MaxPoints,
    [property: JsonPropertyName("suggestedPoints")] decimal? SuggestedPoints,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("evidenceText")] string? EvidenceText,
    [property: JsonPropertyName("gapsText")] string? GapsText,
    [property: JsonPropertyName("teacherReviewRequired")] bool TeacherReviewRequired,
    [property: JsonPropertyName("teacherApproved")] bool TeacherApproved,
    [property: JsonPropertyName("artifactIds")] IReadOnlyList<Guid>? ArtifactIds);

public sealed record AiGradingEvidenceInput(
    [property: JsonPropertyName("artifactId")] Guid ArtifactId,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("quoteHash")] string? QuoteHash);

public static class AiGradingProposalFactory
{
    public static AiGradingProposal Create(
        AssistedGradingItem item,
        AiGradingProposalInput input,
        decimal? authoritativeMaxGrade,
        int version,
        string? fallbackFeedback = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(input);

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var itemContextHash = string.IsNullOrWhiteSpace(item.ContextHash)
            ? null
            : item.ContextHash.Trim();
        var proposalContextHash = string.IsNullOrWhiteSpace(input.ContextHash)
            ? null
            : input.ContextHash.Trim();
        if (!string.Equals(itemContextHash, proposalContextHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A proposta nao corresponde ao contexto versionado do item.");
        }

        var scale = authoritativeMaxGrade is > 0
            ? new GradingScaleSnapshot(authoritativeMaxGrade, "points", "Moodle")
            : null;
        var extraction = input.Extraction ?? new GradingExtractionSummary("missing", 0, false, 0, 0, null);
        var coverage = input.Coverage ?? new GradingEvidenceCoverage(0, 0, 0, 0, 0, 0, true);
        var criteria = (input.Criteria ?? [])
            .Select(criterion => new AiGradingCriterionProposal(
                criterion.CriterionId,
                criterion.Description,
                criterion.MaxPoints,
                criterion.SuggestedPoints,
                criterion.Source,
                criterion.EvidenceText,
                criterion.GapsText,
                criterion.TeacherReviewRequired,
                criterion.TeacherApproved,
                criterion.ArtifactIds ?? []))
            .ToArray();
        var evidence = (input.Evidence ?? [])
            .Select(value => new AiGradingEvidenceReference(value.ArtifactId, value.Reference, value.QuoteHash))
            .ToArray();

        var confidence = AiGradingConfidenceCalculator.Calculate(
            input.ModelConfidence,
            coverage,
            extraction,
            scale,
            criteria);
        var feedback = string.IsNullOrWhiteSpace(input.Feedback)
            ? (string.IsNullOrWhiteSpace(fallbackFeedback) ? item.DraftFeedback : fallbackFeedback)
            : input.Feedback;
        var grade = input.SuggestedGrade;
        if (grade is not null && authoritativeMaxGrade is not > 0)
        {
            throw new InvalidOperationException("Uma nota sugerida exige escala numerica confirmada pelo Moodle.");
        }

        return AiGradingProposal.Create(
            item.Id,
            item.BatchId,
            version,
            itemContextHash,
            grade,
            feedback,
            criteria,
            evidence,
            input.Gaps,
            scale,
            extraction,
            coverage,
            confidence,
            reviewRequired: true,
            status: string.IsNullOrWhiteSpace(input.Status) ? "ready_for_review" : input.Status!);
    }
}
