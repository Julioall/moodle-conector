namespace MoodleConnector.Presentation;

public sealed record AppOperationalReportDto(int OpenTasks, int CompletedTasks, int UpcomingEvents, int FollowupsRecorded, DateTimeOffset GeneratedAt);
public sealed record AppAuditReportDto(int TotalActions, int CompletedActions, int FailedActions, int ConfirmedActions, DateTimeOffset GeneratedAt);

public sealed record AppCourseOverviewReportDto(
    string ConnectionRef, string CourseId, DateTimeOffset GeneratedAt, int TotalActiveStudents,
    int StudentsWhoAccessed, int StudentsNeverAccessed, int StudentsInactiveDays,
    int InactiveDaysThreshold, int TotalGradedItems, decimal AverageBelowMinimumPerStudent,
    IReadOnlyList<string> SuggestedActionsForTutor, string? Warning);

public sealed record AppWeeklyReportDto(
    string ConnectionRef, string CourseId, DateTimeOffset GeneratedAt, int TotalStudents,
    int StudentsWithAttention, int StudentsAtRisk, decimal MinGradePercent,
    int InactiveDaysThreshold, string? Warning);

public sealed record AppCompletionReportDto(
    string ConnectionRef, string CourseId, DateTimeOffset GeneratedAt, int TotalStudents,
    int LikelyComplete, int PendingRecovery, int AtRisk, int Unknown, decimal MinGradePercent,
    string Disclaimer, string? Warning);

