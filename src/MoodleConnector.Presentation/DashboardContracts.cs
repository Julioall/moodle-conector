namespace MoodleConnector.Presentation;

public sealed record AppDashboardSummaryDto(
    int ActiveCourses,
    int PendingDeliveries,
    int AwaitingGrading,
    int StudentsAtRisk,
    int StudentsNeedingAttention)
{
    // Claris-compatible indicators. Nullable values are intentional when the
    // connector has no bounded source for a metric yet.
    public int? TodayEvents { get; init; }
    public int? TodayTasks { get; init; }
    public int? ActivitiesToReview { get; init; }
    public int? ActiveNormalStudents { get; init; }
    public int? PendingSubmissionAssignments { get; init; }
    public int? PendingCorrectionAssignments { get; init; }
    public int? NewAtRiskThisWeek { get; init; }
}

public sealed record AppDashboardPriorityDto(
    string Key,
    string Title,
    string Detail,
    string Level,
    string? CourseId,
    string? StudentId);

public sealed record AppDashboardActivityDto(
    string Key,
    string Title,
    string Detail,
    DateTimeOffset? OccurredAt,
    string? CourseId,
    string? StudentId);

public sealed record AppDashboardDto(
    AppDashboardSummaryDto Summary,
    IReadOnlyList<AppDashboardPriorityDto> Priorities,
    IReadOnlyList<AppDashboardPriorityDto> ActivitiesToReview,
    IReadOnlyList<AppDashboardActivityDto> RecentActivity,
    string? ConnectionRef,
    IReadOnlyList<string> Warnings)
{
    public string Week { get; init; } = AppDashboardWeekFilter.Current;
    public DateTimeOffset? WeekStartsAt { get; init; }
    public DateTimeOffset? WeekEndsAt { get; init; }
}

public static class AppDashboardWeekFilter
{
    public const string Current = "current";
    public const string Last = "last";

    public static string Normalize(string? value) =>
        string.Equals(value, Last, StringComparison.OrdinalIgnoreCase) ? Last : Current;
}

public static class AppDashboardBudget
{
    public const int MaxCoursesRead = 20;
    public const int MaxParticipantsRead = 100;
    public const int MaxAssignmentsRead = 20;
    public const int MaxPriorities = 8;
    public const int MaxActivities = 8;
}

public static class AppDashboardContractMapper
{
    public static AppDashboardDto Empty(string? connectionRef, IEnumerable<string> warnings, IReadOnlyList<AppDashboardActivityDto>? recentActivity = null) => new(
        new(0, 0, 0, 0, 0), [], [], recentActivity ?? [], connectionRef,
        warnings.Distinct(StringComparer.Ordinal).ToArray());
}

