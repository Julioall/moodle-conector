using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleResourceRepository
{
    Task RegisterAsync(MoodleResource resource, CancellationToken cancellationToken);
    Task<MoodleResource?> FindAsync(string resourceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MoodleResource>> ListBySubmissionAsync(
        string clientId,
        string connectionId,
        long submissionId,
        CancellationToken cancellationToken);
    /// <summary>
    /// Verifica se um resource existe e não está expirado nem revogado.
    /// Usado na validação de evidências do grading proposal sem fazer download.
    /// </summary>
    Task<bool> ExistsAndNotExpiredAsync(string resourceId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

