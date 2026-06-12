using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleAssignmentSubmissionsGateway
{
    Task<IReadOnlyList<AssignmentSubmissionRecord>> GetAssignmentSubmissionsAsync(
        string userExternalId,
        string assignmentId,
        string? status,
        DateTimeOffset? since,
        DateTimeOffset? before,
        CancellationToken cancellationToken);
}
