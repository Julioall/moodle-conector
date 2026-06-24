using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleCoursesGateway
{
    Task<PagedCourses> GetMyCoursesAsync(
        string userExternalId,
        int limit,
        int page,
        CancellationToken cancellationToken);

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
