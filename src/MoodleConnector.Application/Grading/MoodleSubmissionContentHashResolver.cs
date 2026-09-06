using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Grading;

internal sealed class MoodleSubmissionContentHashResolver(
    IMoodleAssignmentSubmissionsGateway submissionsGateway) : ISubmissionContentHashResolver
{
    // A saved 10k-item page commonly contains many students from the same
    // assignment. Cache the assignment read for this scoped resolver so the
    // integrity seal does not issue one identical Moodle request per item.
    private readonly Dictionary<(string UserExternalId, string AssignmentId), IReadOnlyList<AssignmentSubmissionRecord>> submissionsCache = [];
    private readonly HashSet<(string UserExternalId, string AssignmentId)> failedLookups = [];

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

        var cacheKey = (userExternalId.Trim(), assignmentId.Trim());
        IReadOnlyList<AssignmentSubmissionRecord> submissions;
        if (submissionsCache.TryGetValue(cacheKey, out var cachedSubmissions))
        {
            submissions = cachedSubmissions;
        }
        else
        {
            if (failedLookups.Contains(cacheKey))
            {
                throw new InvalidOperationException("A leitura da submissao Moodle falhou anteriormente para esta atividade durante a mesma operacao.");
            }

            try
            {
                submissions = await submissionsGateway.GetAssignmentSubmissionsAsync(
                    userExternalId,
                    assignmentId,
                    status: null,
                    since: null,
                    before: null,
                    cancellationToken);
                submissionsCache[cacheKey] = submissions;
            }
            catch
            {
                // Cache the failure for this scoped operation. The caller can
                // retry the whole command with a fresh scope, while sibling
                // items do not hammer an unavailable Moodle endpoint.
                failedLookups.Add(cacheKey);
                throw;
            }
        }
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
