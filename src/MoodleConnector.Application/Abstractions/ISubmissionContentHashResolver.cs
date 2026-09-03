using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Produz a identidade da submissão a partir de metadados atuais do Moodle e
/// hashes já verificados dos arquivos. Não recebe nem persiste bytes.
/// </summary>
public interface ISubmissionContentHashResolver
{
    Task<SubmissionContentHashSnapshot> ResolveAsync(
        string userExternalId,
        string assignmentId,
        string studentId,
        long submissionId,
        IReadOnlyCollection<string> attachmentHashes,
        CancellationToken cancellationToken);
}

public sealed record SubmissionContentHashSnapshot(
    string Hash,
    int? AttemptNumber,
    DateTimeOffset? ModifiedAt,
    int FileCount);
