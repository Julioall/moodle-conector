using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MoodleConnector.Domain.Grading;

public static class AiGradingCriterionSource
{
    public const string FormalRubric = "FormalRubric";
    public const string TeacherDefined = "TeacherDefined";
    public const string StatementDerived = "StatementDerived";
    public const string GeneratedSupport = "GeneratedSupport";
    public const string Unknown = "Unknown";

    public static bool IsKnown(string value) => value switch
    {
        FormalRubric or TeacherDefined or StatementDerived or GeneratedSupport or Unknown => true,
        _ => false
    };
}

public sealed record AiGradingCriterionProposal(
    string CriterionId,
    string Description,
    decimal? MaxPoints,
    decimal? SuggestedPoints,
    string Source,
    string? EvidenceText,
    string? GapsText,
    bool TeacherReviewRequired,
    bool TeacherApproved,
    IReadOnlyList<Guid> ArtifactIds);

public sealed record AiGradingEvidenceReference(
    Guid ArtifactId,
    string? Reference,
    string? QuoteHash);

public sealed record AiGradingConfidenceResult(
    decimal Confidence,
    IReadOnlyList<string> UncertaintyReasons,
    bool ReviewRequired);

/// <summary>
/// Proposta IA versionada e auditável. O modelo pode fornecer uma sugestão,
/// mas o backend valida a escala, a proveniência, a cobertura e a confiança
/// antes de persistir o artefato.
/// </summary>
public sealed class AiGradingProposal
{
    public const string CurrentSchemaVersion = "1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private AiGradingProposal(
        Guid itemId,
        Guid batchId,
        int version,
        string? contextHash,
        decimal? suggestedGrade,
        string? feedback,
        IReadOnlyList<AiGradingCriterionProposal> criteria,
        IReadOnlyList<AiGradingEvidenceReference> evidence,
        IReadOnlyList<string> gaps,
        GradingScaleSnapshot? gradingScale,
        GradingExtractionSummary extraction,
        GradingEvidenceCoverage coverage,
        decimal confidence,
        IReadOnlyList<string> uncertaintyReasons,
        bool reviewRequired,
        string status,
        DateTimeOffset createdAt)
    {
        ItemId = itemId;
        BatchId = batchId;
        Version = version;
        ContextHash = contextHash;
        SuggestedGrade = suggestedGrade;
        Feedback = feedback;
        Criteria = criteria;
        Evidence = evidence;
        Gaps = gaps;
        GradingScale = gradingScale;
        Extraction = extraction;
        Coverage = coverage;
        Confidence = confidence;
        UncertaintyReasons = uncertaintyReasons;
        ReviewRequired = reviewRequired;
        Status = status;
        CreatedAt = createdAt;
        ProposalHash = ComputeHash(this);
    }

    public Guid ItemId { get; }

    public Guid BatchId { get; }

    public int Version { get; }

    public string SchemaVersion => CurrentSchemaVersion;

    public string? ContextHash { get; }

    public decimal? SuggestedGrade { get; }

    public string? Feedback { get; }

    public IReadOnlyList<AiGradingCriterionProposal> Criteria { get; }

    public IReadOnlyList<AiGradingEvidenceReference> Evidence { get; }

    public IReadOnlyList<string> Gaps { get; }

    public GradingScaleSnapshot? GradingScale { get; }

    public GradingExtractionSummary Extraction { get; }

    public GradingEvidenceCoverage Coverage { get; }

    public decimal Confidence { get; }

    public IReadOnlyList<string> UncertaintyReasons { get; }

    public bool ReviewRequired { get; }

    public string Status { get; }

    public DateTimeOffset CreatedAt { get; }

    public string ProposalHash { get; }

    public static AiGradingProposal Create(
        Guid itemId,
        Guid batchId,
        int version,
        string? contextHash,
        decimal? suggestedGrade,
        string? feedback,
        IReadOnlyList<AiGradingCriterionProposal>? criteria,
        IReadOnlyList<AiGradingEvidenceReference>? evidence,
        IReadOnlyList<string>? gaps,
        GradingScaleSnapshot? gradingScale,
        GradingExtractionSummary extraction,
        GradingEvidenceCoverage coverage,
        AiGradingConfidenceResult confidence,
        bool reviewRequired,
        string status = "ready_for_review",
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(confidence);

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("O item e obrigatorio.", nameof(itemId));
        }

        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A versao da proposta deve ser positiva.");
        }

        if (suggestedGrade is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suggestedGrade), "A nota sugerida nao pode ser negativa.");
        }

        if (suggestedGrade is not null &&
            (gradingScale is null || gradingScale.MaximumGrade is not > 0))
        {
            throw new InvalidOperationException("Uma nota sugerida exige escala numerica confirmada.");
        }

        if (suggestedGrade is not null && suggestedGrade > gradingScale!.MaximumGrade)
        {
            throw new ArgumentOutOfRangeException(nameof(suggestedGrade), "A nota sugerida excede a escala confirmada.");
        }

        var normalizedConfidence = confidence.Confidence;
        if (normalizedConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "A confianca deve ficar entre 0 e 1.");
        }

        var normalizedStatus = NormalizeBounded(
            NormalizeRequired(status, nameof(status)),
            80,
            nameof(status))!;
        var normalizedContextHash = NormalizeBounded(contextHash, 64, nameof(contextHash));
        var normalizedFeedback = NormalizeBounded(feedback, 12000, nameof(feedback));
        if (gradingScale?.MaximumGrade is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gradingScale), "A escala confirmada nao pode ser negativa.");
        }
        if (gradingScale?.MaximumGrade is null && suggestedGrade is not null)
        {
            throw new InvalidOperationException("Uma nota sugerida exige escala numerica confirmada.");
        }
        var normalizedCriteria = CopyCriteria(criteria);
        var normalizedEvidence = CopyEvidence(evidence);
        var normalizedGaps = CopyStrings(gaps);
        var normalizedReasons = CopyStrings(confidence.UncertaintyReasons);

        ValidateCriteria(normalizedCriteria, gradingScale);
        if (normalizedConfidence < 0.8m)
        {
            reviewRequired = true;
        }

        return new AiGradingProposal(
            itemId,
            batchId,
            version,
            normalizedContextHash,
            suggestedGrade,
            normalizedFeedback,
            normalizedCriteria,
            normalizedEvidence,
            normalizedGaps,
            gradingScale,
            extraction,
            coverage,
            normalizedConfidence,
            normalizedReasons,
            reviewRequired || confidence.ReviewRequired,
            normalizedStatus,
            createdAt ?? DateTimeOffset.UtcNow);
    }

    public static AiGradingProposal FromLegacy(
        Guid itemId,
        Guid batchId,
        int version,
        string? contextHash,
        decimal? suggestedGrade,
        string? feedback)
    {
        var extraction = new GradingExtractionSummary(
            "missing",
            0,
            false,
            0,
            0,
            null);
        var coverage = new GradingEvidenceCoverage(
            0,
            0,
            0,
            0,
            0,
            0,
            true);
        var confidence = new AiGradingConfidenceResult(
            0m,
            ["legacy_proposal_without_evidence", "backend_confidence_unavailable"],
            true);

        return new AiGradingProposal(
            itemId,
            batchId,
            version,
            Normalize(contextHash),
            suggestedGrade: null,
            NormalizeBounded(feedback, 12000, nameof(feedback)),
            [],
            [],
            ["Proposta legada sem criterios, evidencias ou cobertura estruturada."],
            null,
            extraction,
            coverage,
            confidence.Confidence,
            confidence.UncertaintyReasons,
            reviewRequired: true,
            status: "legacy_review_required",
            DateTimeOffset.UtcNow);
    }

    public static string ComputeHash(AiGradingProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var json = SerializeCanonicalPayload(proposal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static string SerializeCanonicalPayload(AiGradingProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return JsonSerializer.Serialize(
            new CanonicalPayload(
                proposal.SchemaVersion,
                proposal.Version,
                proposal.ItemId,
                proposal.BatchId,
                proposal.ContextHash,
                proposal.SuggestedGrade,
                proposal.Feedback,
                proposal.Criteria,
                proposal.Evidence,
                proposal.Gaps,
                proposal.GradingScale,
                proposal.Extraction,
                proposal.Coverage,
                proposal.Confidence,
                proposal.UncertaintyReasons,
                proposal.ReviewRequired,
                proposal.Status),
            JsonOptions);
    }

    private static void ValidateCriteria(
        IReadOnlyList<AiGradingCriterionProposal> criteria,
        GradingScaleSnapshot? gradingScale)
    {
        foreach (var criterion in criteria)
        {
            if (!AiGradingCriterionSource.IsKnown(criterion.Source))
            {
                throw new ArgumentException("A origem do criterio nao e reconhecida.", nameof(criteria));
            }

            if (criterion.MaxPoints is < 0 || criterion.SuggestedPoints is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(criteria), "Os pontos dos criterios nao podem ser negativos.");
            }

            if (criterion.SuggestedPoints is not null &&
                criterion.MaxPoints is not null &&
                criterion.SuggestedPoints > criterion.MaxPoints)
            {
                throw new ArgumentOutOfRangeException(nameof(criteria), "Os pontos sugeridos excedem o maximo do criterio.");
            }

            if (criterion.Source == AiGradingCriterionSource.GeneratedSupport &&
                criterion.SuggestedPoints is not null &&
                !criterion.TeacherApproved)
            {
                throw new InvalidOperationException("Criterios gerados para apoio nao podem distribuir pontos sem aprovacao humana.");
            }
        }

        if (gradingScale?.MaximumGrade is > 0)
        {
            var scoredPoints = criteria
                .Where(criterion => criterion.Source != AiGradingCriterionSource.GeneratedSupport || criterion.TeacherApproved)
                .Sum(criterion => criterion.SuggestedPoints ?? 0m);
            if (scoredPoints > gradingScale.MaximumGrade)
            {
                throw new ArgumentOutOfRangeException(nameof(criteria), "A soma dos pontos sugeridos excede a escala confirmada.");
            }
        }
    }

    private static IReadOnlyList<AiGradingCriterionProposal> CopyCriteria(
        IReadOnlyList<AiGradingCriterionProposal>? values) =>
        Array.AsReadOnly((values ?? [])
            .Select(value => new AiGradingCriterionProposal(
                NormalizeRequired(value.CriterionId, "criterionId"),
                NormalizeBounded(NormalizeRequired(value.Description, "description"), 2000, "description")!,
                value.MaxPoints,
                value.SuggestedPoints,
                NormalizeRequired(value.Source, "source"),
                NormalizeBounded(value.EvidenceText, 2000, "evidenceText"),
                NormalizeBounded(value.GapsText, 2000, "gapsText"),
                value.TeacherReviewRequired,
                value.TeacherApproved,
                CopyGuids(value.ArtifactIds)))
            .ToArray());

    private static IReadOnlyList<AiGradingEvidenceReference> CopyEvidence(
        IReadOnlyList<AiGradingEvidenceReference>? values) =>
        Array.AsReadOnly((values ?? [])
            .Select(value =>
            {
                if (value.ArtifactId == Guid.Empty)
                {
                    throw new ArgumentException("A evidencia deve referenciar um artifact valido.", "evidence");
                }

                return new AiGradingEvidenceReference(
                    value.ArtifactId,
                    NormalizeBounded(value.Reference, 500, "reference"),
                    NormalizeBounded(value.QuoteHash, 128, "quoteHash"));
            })
            .ToArray());

    private static IReadOnlyList<Guid> CopyGuids(IReadOnlyList<Guid>? values) =>
        Array.AsReadOnly((values ?? []).Where(value => value != Guid.Empty).Distinct().ToArray());

    private static IReadOnlyList<string> CopyStrings(IReadOnlyList<string>? values) =>
        Array.AsReadOnly((values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeBounded(value, 1000, "values")!)
            .Distinct(StringComparer.Ordinal)
            .ToArray());

    private static string? NormalizeBounded(string? value, int maxLength, string parameterName)
    {
        var normalized = Normalize(value);
        if (normalized is not null && normalized.Length > maxLength)
        {
            throw new ArgumentException("O valor excede o limite permitido.", parameterName);
        }

        return normalized;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRequired(string value, string parameterName) =>
        Normalize(value) ?? throw new ArgumentException("O valor e obrigatorio.", parameterName);

    private sealed record CanonicalPayload(
        string SchemaVersion,
        int Version,
        Guid ItemId,
        Guid BatchId,
        string? ContextHash,
        decimal? SuggestedGrade,
        string? Feedback,
        IReadOnlyList<AiGradingCriterionProposal> Criteria,
        IReadOnlyList<AiGradingEvidenceReference> Evidence,
        IReadOnlyList<string> Gaps,
        GradingScaleSnapshot? GradingScale,
        GradingExtractionSummary Extraction,
        GradingEvidenceCoverage Coverage,
        decimal Confidence,
        IReadOnlyList<string> UncertaintyReasons,
        bool ReviewRequired,
        string Status);
}
