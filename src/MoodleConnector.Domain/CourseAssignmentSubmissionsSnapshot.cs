namespace MoodleConnector.Domain;

/// <summary>
/// Durable, course-scoped snapshot of assignment submissions. The snapshot
/// stores normalized rows rather than raw Moodle payloads so read tools can
/// apply filters and pagination without contacting Moodle again.
/// </summary>
public sealed record CourseAssignmentSubmissionsSnapshot(
    string CourseId,
    IReadOnlyList<AssignmentSubmissionsSnapshotItem> Assignments);

public sealed record AssignmentSubmissionsSnapshotItem(
    string AssignmentId,
    string? AssignmentModuleId,
    string AssignmentName,
    DateTimeOffset? DueAt,
    IReadOnlyList<AssignmentSubmissionSummary> Submissions,
    bool IsComplete = true,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    decimal? MaxGrade = null);
