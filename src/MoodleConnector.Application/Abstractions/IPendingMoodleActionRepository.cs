using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IPendingMoodleActionRepository
{
    Task AddAsync(PendingMoodleAction action, CancellationToken cancellationToken);

    Task<PendingMoodleAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
