using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleCoursesGateway
{
    Task<PagedCourses> GetMyCoursesAsync(
        string userExternalId,
        int limit,
        int page,
        CancellationToken cancellationToken);

    Task<PagedCourses> GetMyCoursesAsync(
        string userExternalId,
        int limit,
        int page,
        bool activeOnly,
        CancellationToken cancellationToken) =>
        GetMyCoursesAsync(userExternalId, limit, page, cancellationToken);

    Task<IReadOnlyList<CourseSummary>> SearchMyCoursesAsync(
        string userExternalId,
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<CourseSummary?> GetMyCourseAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken);
}
