using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleAssignmentGradingGateway
{
    Task<AssignmentGradeWriteResult> SaveGradeAsync(
        string userExternalId,
        AssignmentGradeWriteRequest request,
        CancellationToken cancellationToken);
}
