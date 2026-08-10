using MoodleConnector.Domain;

public sealed record PortalCourseRef(string ConnectionRef, string CourseId);
public sealed record PortalCourseDto(
    string ConnectionRef, string CourseId, string? IdNumber, string? ShortName,
    string FullName, string? DisplayName, string? CategoryName,
    DateTimeOffset? StartDate, DateTimeOffset? EndDate, bool? Visible,
    string? ViewUrl, string? CourseImage, decimal? Progress, bool? HasProgress,
    bool? IsFavourite, DateTimeOffset? LastAccessAt);
public sealed record PortalActivityDto(
    string ConnectionRef, string CourseId, string ActivityId, string? InstanceId,
    string ActivityType, string Name, string? Url, bool? Visible, bool? UserVisible,
    string? Description, string? AvailabilityInfo, bool HasDates, bool HasDeadline,
    DateTimeOffset? OpenAt, DateTimeOffset? DueAt, DateTimeOffset? CloseAt, int FileCount);

public static class PortalCourseContractMapper
{
    public static PortalCourseDto ToDto(CourseSummary course, string connectionRef) => new(
        connectionRef, course.CourseId, course.IdNumber, course.ShortName, course.FullName,
        course.DisplayName, course.CategoryName, course.StartDate, course.EndDate,
        course.Visible, course.ViewUrl, course.CourseImage, course.Progress,
        course.HasProgress, course.IsFavourite, course.LastAccessAt);

    public static PortalActivityDto ToDto(CourseActivitySummary activity, string connectionRef, string courseId) => new(
        connectionRef, courseId, activity.ActivityId, activity.InstanceId, activity.ActivityType,
        activity.Name, activity.Url, activity.Visible, activity.UserVisible, activity.Description,
        activity.AvailabilityInfo, activity.HasDates, activity.HasDeadline, activity.OpenAt,
        activity.DueAt, activity.CloseAt, activity.FileCount);
}

public static class PortalErrorResults
{
    public static IResult NotFound(string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: StatusCodes.Status404NotFound);
}
