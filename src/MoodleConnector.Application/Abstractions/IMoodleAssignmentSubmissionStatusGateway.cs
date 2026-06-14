using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleAssignmentSubmissionStatusGateway
{
    Task<AssignmentSubmissionAttemptStatus?> GetSubmissionStatusAsync(
        string userExternalId,
        string assignmentId,
        string studentId,
        CancellationToken cancellationToken);
}
