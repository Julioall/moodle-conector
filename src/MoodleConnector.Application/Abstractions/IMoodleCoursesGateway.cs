using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleCoursesGateway
{
    Task<IReadOnlyList<CourseHierarchyNode>> GetMyCourseHierarchyAsync(string userExternalId, CancellationToken cancellationToken);

    Task<PagedCourses> GetMyCoursesByCategoryAsync(string userExternalId, string categoryPath, int limit, int page, CancellationToken cancellationToken);

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

public sealed record CourseHierarchyNode(string Path, string Name, int Level, int CourseCount);
