using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

public sealed class UserMemoryRepository(ConnectorDbContext dbContext) : IUserMemoryRepository
{
    public Task<UserMemory?> FindEquivalentAsync(
        string ownerSubject,
        string category,
        string? moodleAlias,
        string? courseId,
        string normalizedKey,
        CancellationToken cancellationToken = default)
    {
        return dbContext.UserMemories.SingleOrDefaultAsync(
            memory => memory.OwnerSubject == ownerSubject &&
                      memory.Category == category &&
                      memory.MoodleAlias == moodleAlias &&
                      memory.CourseId == courseId &&
                      memory.NormalizedKey == normalizedKey,
            cancellationToken);
    }

    public async Task AddAsync(UserMemory memory, CancellationToken cancellationToken = default)
    {
        await dbContext.UserMemories.AddAsync(memory, cancellationToken);
    }

    public async Task<IReadOnlyList<UserMemory>> ListAsync(
        string ownerSubject,
        string? moodleAlias,
        string? courseId,
        string? category,
        string? query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var memories = dbContext.UserMemories
            .Where(memory => memory.OwnerSubject == ownerSubject &&
                ((memory.MoodleAlias == null && memory.CourseId == null) ||
                 (moodleAlias != null && memory.MoodleAlias == moodleAlias && memory.CourseId == null) ||
                 (moodleAlias != null && courseId != null &&
                  memory.MoodleAlias == moodleAlias && memory.CourseId == courseId)));

        if (category is not null)
        {
            memories = memories.Where(memory => memory.Category == category);
        }

        if (query is not null)
        {
            if (dbContext.Database.IsNpgsql())
            {
                var pattern = $"%{EscapeLikePattern(query)}%";
                memories = memories.Where(memory =>
                    EF.Functions.ILike(memory.NormalizedKey, pattern, "\\") ||
                    EF.Functions.ILike(memory.Content, pattern, "\\"));
            }
            else
            {
                var normalizedQuery = query.ToLower();
                memories = memories.Where(memory =>
                    memory.NormalizedKey.ToLower().Contains(normalizedQuery) ||
                    memory.Content.ToLower().Contains(normalizedQuery));
            }
        }

        return await memories
            .OrderByDescending(memory => memory.CourseId != null ? 2 : memory.MoodleAlias != null ? 1 : 0)
            .ThenByDescending(memory => memory.UpdatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    public async Task<bool> RemoveAsync(
        Guid id,
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        var memory = await FindOwnedAsync(id, ownerSubject, cancellationToken);
        if (memory is null)
        {
            return false;
        }

        dbContext.UserMemories.Remove(memory);
        return true;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<UserMemory?> FindOwnedAsync(
        Guid id,
        string ownerSubject,
        CancellationToken cancellationToken = default)
    {
        return dbContext.UserMemories.SingleOrDefaultAsync(
            memory => memory.OwnerSubject == ownerSubject && memory.Id == id,
            cancellationToken);
    }
}
