namespace MoodleConnector.Domain;

/// <summary>
/// Canonical state of an assignment submission.  It is intentionally derived
/// from the submission and gradebook evidence together: Moodle's
/// <c>gradingstatus</c> and a null <c>graderaw</c> are not conclusive on their
/// own, particularly for feedback-only assignments.
/// </summary>
public enum SubmissionEvaluationState
{
    Unknown = 0,
    NotSubmitted = 1,
    AwaitingGrading = 2,
    ReviewedWithFeedback = 3,
    GradedNumeric = 4
}

public sealed record SubmissionEvaluationEvidence(
    bool? HasSubmission,
    decimal? GradeRaw,
    long? GradedDateGraded,
    string? Feedback,
    bool ReviewEvidenceAvailable,
    string? GradingStatus = null,
    long? GraderId = null,
    long? GradeTimeModified = null,
    long? SubmissionTimeModified = null);

public static class SubmissionEvaluationStateResolver
{
    public static SubmissionEvaluationState Resolve(SubmissionEvaluationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.HasSubmission == false)
        {
            return SubmissionEvaluationState.NotSubmitted;
        }

        if (evidence.HasSubmission is null)
        {
            return SubmissionEvaluationState.Unknown;
        }

        if (evidence.GradeRaw.HasValue)
        {
            return SubmissionEvaluationState.GradedNumeric;
        }

        if (evidence.GradedDateGraded.HasValue || !string.IsNullOrWhiteSpace(evidence.Feedback))
        {
            return SubmissionEvaluationState.ReviewedWithFeedback;
        }

        if (string.Equals(evidence.GradingStatus?.Trim(), "graded", StringComparison.OrdinalIgnoreCase))
        {
            return SubmissionEvaluationState.ReviewedWithFeedback;
        }

        if (evidence.GraderId is > 0 &&
            (evidence.GradeTimeModified is null || evidence.SubmissionTimeModified is null ||
             evidence.GradeTimeModified >= evidence.SubmissionTimeModified))
        {
            return SubmissionEvaluationState.ReviewedWithFeedback;
        }

        // A real submitted submission is enough to classify an ungraded item
        // as awaiting correction. Gradebook availability must not turn it into
        // Unknown, otherwise feedback-only assignments disappear from queues.
        return SubmissionEvaluationState.AwaitingGrading;
    }

    public static bool NeedsGrading(SubmissionEvaluationState state) =>
        state == SubmissionEvaluationState.AwaitingGrading;
}
