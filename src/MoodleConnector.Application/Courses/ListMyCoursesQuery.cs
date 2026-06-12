using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Courses;

public sealed record ListMyCoursesQuery(string UserExternalId, int Limit) : IRequest<IReadOnlyList<CourseSummary>>;

public sealed record SearchCoursesQuery(string UserExternalId, string Query, int Limit) : IRequest<IReadOnlyList<CourseSummary>>;

public sealed record GetCourseQuery(string UserExternalId, string CourseId) : IRequest<CourseSummary?>;

public sealed class ListMyCoursesQueryHandler(IMoodleCoursesGateway gateway)
    : IRequestHandler<ListMyCoursesQuery, IReadOnlyList<CourseSummary>>
{
    public Task<IReadOnlyList<CourseSummary>> Handle(ListMyCoursesQuery request, CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(request.Limit, 1, 20);
        return gateway.GetMyCoursesAsync(request.UserExternalId, safeLimit, cancellationToken);
    }
}

public sealed class SearchCoursesQueryHandler(IMoodleCoursesGateway gateway)
    : IRequestHandler<SearchCoursesQuery, IReadOnlyList<CourseSummary>>
{
    public Task<IReadOnlyList<CourseSummary>> Handle(SearchCoursesQuery request, CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(request.Limit, 1, 20);
        return gateway.SearchMyCoursesAsync(request.UserExternalId, request.Query, safeLimit, cancellationToken);
    }
}

public sealed class GetCourseQueryHandler(IMoodleCoursesGateway gateway)
    : IRequestHandler<GetCourseQuery, CourseSummary?>
{
    public Task<CourseSummary?> Handle(GetCourseQuery request, CancellationToken cancellationToken)
    {
        return gateway.GetMyCourseAsync(request.UserExternalId, request.CourseId, cancellationToken);
    }
}
