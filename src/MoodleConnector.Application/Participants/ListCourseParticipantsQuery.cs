using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Participants;

public sealed record ListCourseParticipantsQuery(
    string UserExternalId,
    string CourseId,
    ParticipantStatusFilter StatusFilter,
    int Page,
    int PageSize,
    bool StudentsOnly,
    bool IncludeEmail) : IRequest<CourseParticipantsPage?>;

public sealed record ListCourseGroupsQuery(
    string UserExternalId,
    string CourseId) : IRequest<IReadOnlyList<CourseGroupSummary>?>;

public sealed record ListGroupMembersQuery(
    string UserExternalId,
    string CourseId,
    string GroupId,
    ParticipantStatusFilter StatusFilter,
    int Page,
    int PageSize,
    bool IncludeEmail) : IRequest<CourseParticipantsPage?>;

public sealed class ListCourseParticipantsQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleParticipantsGateway participantsGateway)
    : IRequestHandler<ListCourseParticipantsQuery, CourseParticipantsPage?>
{
    public async Task<CourseParticipantsPage?> Handle(
        ListCourseParticipantsQuery request,
        CancellationToken cancellationToken)
    {
        var course = await coursesGateway.GetMyCourseAsync(
            request.UserExternalId,
            request.CourseId,
            cancellationToken);
        if (course is null)
        {
            return null;
        }

        return await participantsGateway.GetCourseParticipantsAsync(
            request.UserExternalId,
            course.CourseId,
            request.StatusFilter,
            NormalizePage(request.Page),
            NormalizePageSize(request.PageSize),
            request.StudentsOnly,
            request.IncludeEmail,
            groupId: null,
            cancellationToken);
    }

    internal static int NormalizePage(int page) => Math.Max(1, page);

    internal static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 50);
}

public sealed class ListCourseGroupsQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleParticipantsGateway participantsGateway)
    : IRequestHandler<ListCourseGroupsQuery, IReadOnlyList<CourseGroupSummary>?>
{
    public async Task<IReadOnlyList<CourseGroupSummary>?> Handle(
        ListCourseGroupsQuery request,
        CancellationToken cancellationToken)
    {
        var course = await coursesGateway.GetMyCourseAsync(
            request.UserExternalId,
            request.CourseId,
            cancellationToken);
        if (course is null)
        {
            return null;
        }

        return await participantsGateway.GetCourseGroupsAsync(
            request.UserExternalId,
            course.CourseId,
            cancellationToken);
    }
}

public sealed class ListGroupMembersQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleParticipantsGateway participantsGateway)
    : IRequestHandler<ListGroupMembersQuery, CourseParticipantsPage?>
{
    public async Task<CourseParticipantsPage?> Handle(
        ListGroupMembersQuery request,
        CancellationToken cancellationToken)
    {
        var course = await coursesGateway.GetMyCourseAsync(
            request.UserExternalId,
            request.CourseId,
            cancellationToken);
        if (course is null)
        {
            return null;
        }

        return await participantsGateway.GetCourseParticipantsAsync(
            request.UserExternalId,
            course.CourseId,
            request.StatusFilter,
            ListCourseParticipantsQueryHandler.NormalizePage(request.Page),
            ListCourseParticipantsQueryHandler.NormalizePageSize(request.PageSize),
            studentsOnly: false,
            request.IncludeEmail,
            request.GroupId,
            cancellationToken);
    }
}
