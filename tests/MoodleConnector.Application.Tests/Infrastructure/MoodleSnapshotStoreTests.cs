using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleSnapshotStoreTests
{
    [Fact]
    public async Task SaveAsync_GravaConnectionIdEstavelELePorEsseId()
    {
        var ownerId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"snapshot-store-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ConnectorDbContext(options);
        db.UserAccounts.Add(new UserAccountEntity
        {
            Id = ownerId,
            Name = "Owner",
            Email = $"{ownerId:N}@example.test",
            PasswordHash = "hash",
            ConnectorClientId = "client-1",
        });
        db.ConnectorClients.Add(new ConnectorClientCredentialEntity
        {
            Id = "connection-1",
            ClientId = "client-1",
            MoodleAlias = "goias",
            MoodleBaseUrl = "https://moodle.example",
            MoodleUsernameEncrypted = "user",
            MoodlePasswordEncrypted = "password",
            MoodleTarget = "goias",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var metrics = new MoodleSnapshotMetrics();
        var store = new MoodleSnapshotStore(db, cache, metrics, NullLogger<MoodleSnapshotStore>.Instance);
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(
            ownerId,
            "goias",
            "courses",
            string.Empty,
            new Dictionary<string, string> { ["state"] = "published" },
            "warm",
            frozen: false,
            complete: true,
            recordCount: 1,
            now);

        var entity = await db.MoodleSnapshots.SingleAsync();
        Assert.Equal("connection-1", entity.ConnectionId);

        var read = await store.GetAsync<Dictionary<string, string>>(ownerId, "goias", "courses");
        Assert.NotNull(read);
        Assert.Equal("published", read!.Data["state"]);
    }

    [Fact]
    public async Task GetAsync_FazFallbackControladoParaRegistroLegadoSemConnectionId()
    {
        var ownerId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"snapshot-legacy-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ConnectorDbContext(options);
        db.MoodleSnapshots.Add(new MoodleSnapshotEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            ConnectionId = string.Empty,
            ConnectionAlias = "legado",
            SnapshotType = "courses",
            CourseId = string.Empty,
            PayloadJson = "{\"state\":\"legacy\"}",
            Tier = "warm",
            UpdatedAt = DateTimeOffset.UtcNow,
            FreshUntil = DateTimeOffset.UtcNow.AddHours(1),
            StaleUntil = DateTimeOffset.UtcNow.AddHours(2),
        });
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var metrics = new MoodleSnapshotMetrics();
        var store = new MoodleSnapshotStore(db, cache, metrics, NullLogger<MoodleSnapshotStore>.Instance);

        var read = await store.GetAsync<Dictionary<string, string>>(ownerId, "legado", "courses");

        Assert.NotNull(read);
        Assert.Equal("legacy", read!.Data["state"]);
    }
}
