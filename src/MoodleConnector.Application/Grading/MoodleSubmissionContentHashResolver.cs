using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Grading;

internal sealed class MoodleSubmissionContentHashResolver(
    IMoodleAssignmentSubmissionsGateway submissionsGateway) : ISubmissionContentHashResolver
{
    public async Task<SubmissionContentHashSnapshot> ResolveAsync(
        string userExternalId,
        string assignmentId,
        string studentId,
        long submissionId,
        IReadOnlyCollection<string> attachmentHashes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userExternalId) ||
            string.IsNullOrWhiteSpace(assignmentId) ||
            string.IsNullOrWhiteSpace(studentId) ||
            submissionId <= 0)
        {
            throw new ArgumentException("Os identificadores da submissao sao obrigatorios para calcular a integridade.");
        }

        var submissions = await submissionsGateway.GetAssignmentSubmissionsAsync(
            userExternalId,
            assignmentId,
            status: null,
            since: null,
            before: null,
            cancellationToken);
        var submission = submissions.SingleOrDefault(candidate =>
            string.Equals(candidate.SubmissionId, submissionId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
            string.Equals(candidate.UserId, studentId, StringComparison.Ordinal));
        if (submission is null)
        {
            throw new InvalidOperationException("A submissao nao esta mais disponivel para validar a integridade.");
        }

        var hash = SubmissionContentHash.Compute(
            attachmentHashes,
            submission.OnlineText,
            submission.AttemptNumber,
            submission.ModifiedAt);
        return new SubmissionContentHashSnapshot(hash, submission.AttemptNumber, submission.ModifiedAt, submission.FileCount);
    }
}
