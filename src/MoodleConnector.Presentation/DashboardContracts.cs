namespace MoodleConnector.Presentation;

public sealed record AppDashboardSummaryDto(
    int ActiveCourses,
    int PendingDeliveries,
    int AwaitingGrading,
    int StudentsAtRisk,
    int StudentsNeedingAttention);

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
    IReadOnlyList<string> Warnings);

public static class AppDashboardBudget
{
    public const int MaxCoursesRead = 20;
    public const int MaxParticipantsRead = 100;
    public const int MaxPriorities = 8;
    public const int MaxActivities = 8;
}

public static class AppDashboardContractMapper
{
    public static AppDashboardDto Empty(string? connectionRef, IEnumerable<string> warnings) => new(
        new(0, 0, 0, 0, 0), [], [], [], connectionRef,
        warnings.Distinct(StringComparer.Ordinal).ToArray());
}

