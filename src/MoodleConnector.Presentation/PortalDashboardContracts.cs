namespace MoodleConnector.Presentation;

public sealed record PortalDashboardSummaryDto(
    int ActiveCourses,
    int PendingDeliveries,
    int AwaitingGrading,
    int StudentsAtRisk,
    int StudentsNeedingAttention);

public sealed record PortalDashboardPriorityDto(
    string Key,
    string Title,
    string Detail,
    string Level,
    string? CourseId,
    string? StudentId);

public sealed record PortalDashboardActivityDto(
    string Key,
    string Title,
    string Detail,
    DateTimeOffset? OccurredAt,
    string? CourseId,
    string? StudentId);

public sealed record PortalDashboardDto(
    PortalDashboardSummaryDto Summary,
    IReadOnlyList<PortalDashboardPriorityDto> Priorities,
    IReadOnlyList<PortalDashboardPriorityDto> ActivitiesToReview,
    IReadOnlyList<PortalDashboardActivityDto> RecentActivity,
    string? ConnectionRef,
    IReadOnlyList<string> Warnings);

public static class PortalDashboardBudget
{
    public const int MaxCoursesRead = 20;
    public const int MaxParticipantsRead = 100;
    public const int MaxPriorities = 8;
    public const int MaxActivities = 8;
}

public static class PortalDashboardContractMapper
{
    public static PortalDashboardDto Empty(string? connectionRef, IEnumerable<string> warnings) => new(
        new(0, 0, 0, 0, 0), [], [], [], connectionRef,
        warnings.Distinct(StringComparer.Ordinal).ToArray());
}
