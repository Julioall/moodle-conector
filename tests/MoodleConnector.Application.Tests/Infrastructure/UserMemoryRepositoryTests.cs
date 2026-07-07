using Microsoft.EntityFrameworkCore;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class UserMemoryRepositoryTests
{
    [Fact]
    public async Task ListAsync_IncluiEscoposAplicaveisOrdenadosPorEspecificidadeESomenteDoOwner()
    {
        await using var dbContext = CreateDbContext();
        var repository = new UserMemoryRepository(dbContext);
        var now = DateTimeOffset.UtcNow;
        dbContext.UserMemories.AddRange(
            Memory("alice", "global", null, null, now.AddMinutes(3)),
            Memory("alice", "alias", "principal", null, now.AddMinutes(2)),
            Memory("alice", "curso", "principal", "42", now.AddMinutes(1)),
            Memory("alice", "outro-curso", "principal", "99", now.AddMinutes(4)),
            Memory("bob", "bob", "principal", "42", now.AddMinutes(5)));
        await dbContext.SaveChangesAsync();

        var result = await repository.ListAsync("alice", "principal", "42", 10);

        Assert.Equal(["curso", "alias", "global"], result.Select(memory => memory.NormalizedKey));
    }

    [Fact]
    public async Task FindEquivalentAsync_ExigeOwnerEEscopoExatos()
    {
        await using var dbContext = CreateDbContext();
        var repository = new UserMemoryRepository(dbContext);
        var expected = Memory("alice", "tom", "principal", null, DateTimeOffset.UtcNow);
        dbContext.UserMemories.AddRange(expected, Memory("bob", "tom", "principal", null, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        var result = await repository.FindEquivalentAsync("alice", "preferencia", "principal", null, "tom");

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task RemoveAsync_NaoRemoveMemoriaDeOutroOwner()
    {
        await using var dbContext = CreateDbContext();
        var repository = new UserMemoryRepository(dbContext);
        var memory = Memory("alice", "tom", null, null, DateTimeOffset.UtcNow);
        dbContext.UserMemories.Add(memory);
        await dbContext.SaveChangesAsync();

        var removed = await repository.RemoveAsync(memory.Id, "bob");

        Assert.False(removed);
        Assert.Contains(memory, dbContext.UserMemories);
    }

    private static ConnectorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ConnectorDbContext(options);
    }

    private static UserMemory Memory(
        string owner,
        string key,
        string? alias,
        string? courseId,
        DateTimeOffset updatedAt)
    {
        var memory = new UserMemory(owner, "preferencia", key, key, "explicit", alias, courseId, updatedAt.AddHours(-1));
        memory.Update(key, "explicit", updatedAt);
        return memory;
    }
}
