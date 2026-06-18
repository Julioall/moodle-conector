using MediatR;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Gradebook.Queries;

public sealed record GetStudentGradebookQuery(string CourseId, string StudentId) : IRequest<CourseGradebook>;

public sealed class GetStudentGradebookQueryHandler(IMoodleGradebookGateway gateway)
    : IRequestHandler<GetStudentGradebookQuery, CourseGradebook>
{
    public Task<CourseGradebook> Handle(GetStudentGradebookQuery request, CancellationToken cancellationToken)
    {
        return gateway.GetStudentGradebookAsync(request.CourseId, request.StudentId, cancellationToken);
    }
}
