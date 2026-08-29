using MoodleConnector.Domain;
using MoodleConnector.Application.MoodleApi;

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
        const int maxConcurrentFallbackReads = 4;
        using var gate = new SemaphoreSlim(maxConcurrentFallbackReads, maxConcurrentFallbackReads);
        var results = await Task.WhenAll(assignmentIds.Select(async assignmentId =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var submissions = await GetAssignmentSubmissionsAsync(
                    userExternalId,
                    assignmentId,
                    status,
                    since,
                    before,
                    cancellationToken);
                return new AssignmentSubmissionsBatch(assignmentId, submissions);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = MoodleErrorContract.Describe(exception);
                return new AssignmentSubmissionsBatch(
                    assignmentId,
                    [],
                    failure.ErrorCode,
                    failure.Message);
            }
            finally
            {
                gate.Release();
            }
        }));

        return results;
    }
}

public sealed record AssignmentSubmissionsBatch(
    string AssignmentId,
    IReadOnlyList<AssignmentSubmissionRecord> Submissions,
    string? ErrorCode = null,
    string? ErrorMessage = null);
