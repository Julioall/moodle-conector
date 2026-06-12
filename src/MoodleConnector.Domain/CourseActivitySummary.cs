namespace MoodleConnector.Domain;

public static class CourseActivityModuleTypes
{
    public static readonly IReadOnlyCollection<string> All = ["assign", "quiz", "scorm", "forum"];

    public static readonly IReadOnlyCollection<string> Assignments = ["assign"];

    public static readonly IReadOnlyCollection<string> Quizzes = ["quiz"];

    public static readonly IReadOnlyCollection<string> Scorms = ["scorm"];
}

public sealed record CourseActivitiesSummary(
    string CourseId,
    IReadOnlyCollection<string> ActivityTypeFilters,
    bool IncludeHidden,
    int Total,
    int WithoutDatesCount,
    int WithoutDeadlineCount,
    IReadOnlyList<CourseActivitySummary> Activities);

public sealed record CourseActivitySummary(
    string ActivityId,
    string? InstanceId,
    string ActivityType,
    string Name,
    string? Url,
    bool? Visible,
    bool? UserVisible,
    string? Description,
    string? AvailabilityInfo,
    bool HasDates,
    bool HasDeadline,
    DateTimeOffset? OpenAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? CloseAt,
    IReadOnlyList<CourseModuleDate> Dates,
    int FileCount);

public sealed record CourseActivityDeadlinesSummary(
    string CourseId,
    IReadOnlyCollection<string> ActivityTypeFilters,
    bool IncludeHidden,
    int Total,
    int WithoutDatesCount,
    int WithoutDeadlineCount,
    IReadOnlyList<CourseActivityDeadlineSummary> Deadlines);

public sealed record CourseActivityDeadlineSummary(
    string ActivityId,
    string? InstanceId,
    string ActivityType,
    string Name,
    bool? Visible,
    bool? UserVisible,
    bool HasDates,
    bool HasDeadline,
    DateTimeOffset? OpenAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? CloseAt,
    IReadOnlyList<CourseModuleDate> Dates);
