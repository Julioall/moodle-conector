using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleAssignmentSubmissionsGateway
{
    Task<IReadOnlyList<AssignmentSubmissionsBatch>> GetAssignmentSubmissionsBatchAsync(
        string userExternalId,
        IReadOnlyCollection<string> assignmentIds,
        string? status,
        DateTimeOffset? since,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        return GetAssignmentSubmissionsBatchFallbackAsync(
            userExternalId,
            assignmentIds,
            status,
            since,
            before,
            cancellationToken);
    }

    Task<IReadOnlyList<AssignmentSubmissionRecord>> GetAssignmentSubmissionsAsync(
        string userExternalId,
        string assignmentId,
        string? status,
        DateTimeOffset? since,
        DateTimeOffset? before,
        CancellationToken cancellationToken);

    private async Task<IReadOnlyList<AssignmentSubmissionsBatch>> GetAssignmentSubmissionsBatchFallbackAsync(
        string userExternalId,
        IReadOnlyCollection<string> assignmentIds,
        string? status,
        DateTimeOffset? since,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        var result = new List<AssignmentSubmissionsBatch>(assignmentIds.Count);
        foreach (var assignmentId in assignmentIds)
        {
            var submissions = await GetAssignmentSubmissionsAsync(
                userExternalId,
                assignmentId,
                status,
                since,
                before,
                cancellationToken);
            result.Add(new AssignmentSubmissionsBatch(assignmentId, submissions));
        }

        return result;
    }
}

public sealed record AssignmentSubmissionsBatch(
    string AssignmentId,
    IReadOnlyList<AssignmentSubmissionRecord> Submissions,
    string? ErrorCode = null,
    string? ErrorMessage = null);
