using MediatR;
using MoodleConnector.Application.Courses;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Application.Participants;
using MoodleConnector.Domain;

public sealed class PortalStudentService(IMediator mediator)
{
    public async Task<IReadOnlyList<(string ConnectionRef, CourseParticipantSummary Participant, string CourseId)>>
        ListAsync(string userId, string connectionRef, string? courseId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(courseId))
        {
            var page = await mediator.Send(new ListCourseParticipantsQuery(userId, courseId,
                ParticipantStatusFilter.Active, 1, 100, true, true), cancellationToken);
            return page?.Participants.Select(p => (connectionRef, p, courseId)).ToArray()
                ?? Array.Empty<(string, CourseParticipantSummary, string)>();
        }

        var courses = await mediator.Send(new ListMyCoursesQuery(userId, 100, 1), cancellationToken);
        var rows = new List<(string, CourseParticipantSummary, string)>();
        foreach (var course in courses.Items)
        {
            var page = await mediator.Send(new ListCourseParticipantsQuery(userId, course.CourseId,
                ParticipantStatusFilter.Active, 1, 100, true, true), cancellationToken);
            if (page is not null) rows.AddRange(page.Participants.Select(p => (connectionRef, p, course.CourseId)));
        }
        return rows;
    }

    public async Task<PortalStudentDto?> GetAsync(string userId, string connectionRef, string studentId,
        CancellationToken cancellationToken)
    {
        var rows = await ListAsync(userId, connectionRef, null, cancellationToken);
        var studentRows = rows.Where(r => r.Participant.UserId == studentId).ToArray();
        if (studentRows.Length == 0) return null;

        var first = studentRows[0].Participant;
        var courseDtos = new List<PortalStudentCourseDto>();
        foreach (var row in studentRows.GroupBy(r => r.CourseId).Select(g => g.First()))
        {
            var gradeItems = await mediator.Send(new GetStudentGradeItemsQuery(row.CourseId, studentId), cancellationToken);
            courseDtos.Add(new(row.ConnectionRef, row.CourseId, row.CourseId, null,
                row.Participant.Suspended == true ? "suspenso" : "ativo", null,
                row.Participant.LastCourseAccessAt,
                gradeItems.Items.Select(PortalStudentContractMapper.ToGradeDto).ToArray()));
        }
        return PortalStudentContractMapper.ToDto(connectionRef, first, courseDtos);
    }
}
