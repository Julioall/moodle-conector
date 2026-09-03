using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Submissions;

public sealed class SubmissionEvaluationStateResolverTests
{
    [Fact]
    public void Resolve_matches_the_FIEG_raw_control_fixtures()
    {
        var fixtures = new[]
        {
            new RawFixture("117487", "440752", true, null, 1787075877, "feedback", SubmissionEvaluationState.ReviewedWithFeedback),
            new RawFixture("117487", "440750", true, null, 1787075877, "feedback", SubmissionEvaluationState.ReviewedWithFeedback),
            new RawFixture("117487", "440739", true, null, null, "", SubmissionEvaluationState.AwaitingGrading),
            new RawFixture("117487", "440720", true, null, null, "", SubmissionEvaluationState.AwaitingGrading),
            new RawFixture("117485", "440750", true, 30m, 1787074836, "feedback", SubmissionEvaluationState.GradedNumeric),
            new RawFixture("117485", "440752", true, null, null, "", SubmissionEvaluationState.AwaitingGrading),
            new RawFixture("117485", "440739", false, null, null, "", SubmissionEvaluationState.NotSubmitted)
        };

        foreach (var fixture in fixtures)
        {
            var state = SubmissionEvaluationStateResolver.Resolve(new SubmissionEvaluationEvidence(
                fixture.Submitted, fixture.GradeRaw, fixture.GradedDateGraded, fixture.Feedback, true));
            Assert.Equal(fixture.Expected, state);
            Assert.Equal(fixture.Expected == SubmissionEvaluationState.AwaitingGrading,
                SubmissionEvaluationStateResolver.NeedsGrading(state));
        }
    }

    [Theory]
    [InlineData(true, 30, 1787074836L, "feedback", true, SubmissionEvaluationState.GradedNumeric)]
    [InlineData(true, null, 1787075877L, "feedback", true, SubmissionEvaluationState.ReviewedWithFeedback)]
    [InlineData(true, null, null, null, true, SubmissionEvaluationState.AwaitingGrading)]
    [InlineData(false, null, null, null, true, SubmissionEvaluationState.NotSubmitted)]
    [InlineData(true, null, null, "feedback only", true, SubmissionEvaluationState.ReviewedWithFeedback)]
    [InlineData(true, null, null, null, false, SubmissionEvaluationState.AwaitingGrading)]
    public void Resolve_uses_submission_and_review_evidence(
        bool hasSubmission,
        object? gradeRaw,
        long? gradedDateGraded,
        string? feedback,
        bool evidenceAvailable,
        SubmissionEvaluationState expected)
    {
        var result = SubmissionEvaluationStateResolver.Resolve(new SubmissionEvaluationEvidence(
            hasSubmission,
            gradeRaw is null ? null : Convert.ToDecimal(gradeRaw),
            gradedDateGraded,
            feedback,
            evidenceAvailable));

        Assert.Equal(expected, result);
        Assert.Equal(expected == SubmissionEvaluationState.AwaitingGrading,
            SubmissionEvaluationStateResolver.NeedsGrading(result));
    }

    private sealed record RawFixture(
        string AssignmentId,
        string StudentId,
        bool Submitted,
        decimal? GradeRaw,
        long? GradedDateGraded,
        string Feedback,
        SubmissionEvaluationState Expected);
}
