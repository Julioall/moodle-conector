namespace MoodleConnector.Domain;

public enum AssignmentSubmissionFilter
{
    All = 0,
    Submitted = 1,
    NotSubmitted = 2,
    Late = 3,
    NeedsGrading = 4
}

public sealed record AssignmentSubmissionsPage(
    string CourseId,
    string AssignmentId,
    string? AssignmentModuleId,
    string AssignmentName,
    int Page,
    int PageSize,
    AssignmentSubmissionFilter Filter,
    bool IncludeLate,
    bool IncludeUngraded,
    DateTimeOffset? Since,
    DateTimeOffset? Before,
    int Total,
    bool HasMore,
    IReadOnlyList<AssignmentSubmissionSummary> Submissions);

public sealed record AssignmentSubmissionSummary(
    string UserId,
    string? FullName,
    string? SubmissionId,
    string Status,
    string? GradingStatus,
    bool Submitted,
    bool Late,
    bool NeedsGrading,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ModifiedAt,
    int? AttemptNumber,
    int FileCount,
    bool HasOnlineText,
    IReadOnlyList<AssignmentSubmissionFile>? Files = null,
    decimal? CurrentGrade = null,
    string? CurrentFeedback = null,
    decimal? GradeMax = null,
    SubmissionEvaluationState EvaluationState = SubmissionEvaluationState.Unknown);

public sealed record AssignmentSubmissionFile(
    string Filename,
    string? MimeType,
    long? SizeBytes,
    string FileUrl);

public sealed record AssignmentSubmissionRecord(
    string SubmissionId,
    string UserId,
    string Status,
    string? GradingStatus,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    int? AttemptNumber,
    int FileCount,
    bool HasOnlineText,
    IReadOnlyList<AssignmentSubmissionFile>? Files = null,
    string? CurrentFeedback = null);
