using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IUserMemoryRepository
{
    Task<UserMemory?> FindEquivalentAsync(string ownerSubject, string category, string? moodleAlias, string? courseId, string normalizedKey, CancellationToken cancellationToken = default);
    Task AddAsync(UserMemory memory, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMemory>> ListAsync(string ownerSubject, string? moodleAlias, string? courseId, string? category, string? query, int limit, CancellationToken cancellationToken = default);
    Task<UserMemory?> FindOwnedAsync(Guid id, string ownerSubject, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid id, string ownerSubject, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
