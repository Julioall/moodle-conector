using MediatR;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Completion.Queries;

public sealed record GetStudentCompletionQuery(string CourseId, string StudentId) : IRequest<CourseCompletionStatus>;

public sealed class GetStudentCompletionQueryHandler(IMoodleCompletionGateway gateway)
    : IRequestHandler<GetStudentCompletionQuery, CourseCompletionStatus>
{
    public Task<CourseCompletionStatus> Handle(GetStudentCompletionQuery request, CancellationToken cancellationToken)
    {
        return gateway.GetStudentCompletionAsync(request.CourseId, request.StudentId, cancellationToken);
    }
}
