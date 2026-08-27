using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Domain;

public sealed class AiGradingProposalTests
{
    [Fact]
    public void Create_GeraHashDeterministicoEExigeRevisaoQuandoConfiancaBaixa()
    {
        var criteria = new[]
        {
            new AiGradingCriterionProposal(
                "C1",
                "Aplicacao do conceito",
                10m,
                8m,
                AiGradingCriterionSource.FormalRubric,
                "artifact evidence",
                null,
                TeacherReviewRequired: false,
                TeacherApproved: false,
                ArtifactIds: [])
        };
        var coverage = CompleteCoverage();
        var extraction = CompleteExtraction();
        var scale = new GradingScaleSnapshot(10m, "points", "moodle");
        var confidence = AiGradingConfidenceCalculator.Calculate(
            0.9m,
            coverage,
            extraction,
            scale,
            criteria);

        var first = AiGradingProposal.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "context-hash",
            8m,
            "Feedback",
            criteria,
            [],
            [],
            scale,
            extraction,
            coverage,
            confidence,
            reviewRequired: false,
            createdAt: DateTimeOffset.UtcNow);
        var second = AiGradingProposal.Create(
            first.ItemId,
            first.BatchId,
            first.Version,
            first.ContextHash,
            first.SuggestedGrade,
            first.Feedback,
            first.Criteria,
            first.Evidence,
            first.Gaps,
            first.GradingScale,
            first.Extraction,
            first.Coverage,
            confidence,
            reviewRequired: false,
            createdAt: DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(first.ProposalHash, second.ProposalHash);
        Assert.Equal(0.9m, first.Confidence);
        Assert.False(first.ReviewRequired);
    }

    [Fact]
    public void Create_NaoAceitaNotaSemEscalaConfirmada()
    {
        Assert.Throws<InvalidOperationException>(() => AiGradingProposal.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            null,
            5m,
            "Feedback",
            [],
            [],
            [],
            gradingScale: null,
            CompleteExtraction(),
            CompleteCoverage(),
            new AiGradingConfidenceResult(0m, ["grading_scale_unconfirmed"], true),
            reviewRequired: true));
    }

    [Fact]
    public void Create_NaoPermiteCriterioGeradoDistribuirPontosSemAprovacao()
    {
        var criterion = new AiGradingCriterionProposal(
            "G1",
            "Criterio auxiliar",
            10m,
            4m,
            AiGradingCriterionSource.GeneratedSupport,
            null,
            null,
            TeacherReviewRequired: true,
            TeacherApproved: false,
            ArtifactIds: []);

        Assert.Throws<InvalidOperationException>(() => AiGradingProposal.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            null,
            null,
            "Feedback",
            [criterion],
            [],
            [],
            null,
            CompleteExtraction(),
            CompleteCoverage(),
            new AiGradingConfidenceResult(0.2m, ["generated_criteria_not_approved"], true),
            reviewRequired: true));
    }

    [Fact]
    public void FromLegacy_NaoPromoveNotaNemConfianca()
    {
        var proposal = AiGradingProposal.FromLegacy(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "context-hash",
            8m,
            "Feedback legado");

        Assert.Null(proposal.SuggestedGrade);
        Assert.Equal(0m, proposal.Confidence);
        Assert.True(proposal.ReviewRequired);
        Assert.Equal("legacy_review_required", proposal.Status);
        Assert.Contains("legacy_proposal_without_evidence", proposal.UncertaintyReasons);
    }

    [Fact]
    public void ConfidenceDiminuiComCoberturaParcialEEscalaDesconhecida()
    {
        var full = AiGradingConfidenceCalculator.Calculate(
            0.9m,
            CompleteCoverage(),
            CompleteExtraction(),
            new GradingScaleSnapshot(10m, "points", "moodle"),
            [new AiGradingCriterionProposal("C1", "Criterio", 10m, 8m, AiGradingCriterionSource.FormalRubric, null, null, false, false, [])]);
        var partial = AiGradingConfidenceCalculator.Calculate(
            0.9m,
            new GradingEvidenceCoverage(2, 1, 2, 1, 100, 25, true),
            new GradingExtractionSummary("partial", 2, true, 100, 25, "limit"),
            null,
            [new AiGradingCriterionProposal("C1", "Criterio", null, null, AiGradingCriterionSource.StatementDerived, null, "gap", true, false, [])]);

        Assert.True(full.Confidence > partial.Confidence);
        Assert.True(partial.ReviewRequired);
        Assert.Contains("grading_scale_unconfirmed", partial.UncertaintyReasons);
        Assert.Contains("evidence_coverage_partial", partial.UncertaintyReasons);
    }

    private static GradingEvidenceCoverage CompleteCoverage() =>
        new(1, 1, 1, 1, 100, 100, false);

    private static GradingExtractionSummary CompleteExtraction() =>
        new("succeeded", 1, false, 100, 100, null);
}
