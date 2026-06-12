using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleCoursesGateway
{
    Task<IReadOnlyList<CourseSummary>> GetMyCoursesAsync(
        string userExternalId,
        int limit,
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
