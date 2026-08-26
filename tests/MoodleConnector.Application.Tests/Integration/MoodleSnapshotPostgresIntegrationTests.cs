using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Integration;

public sealed class MoodleSnapshotPostgresIntegrationTests
{
    [Fact]
    public async Task SchemaAndConcurrentHeadUpsertConvergemNoPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // The regular developer suite has no database daemon. CI supplies
            // the connection string and executes this same test against a
            // disposable PostgreSQL service.
            return;
        }

        var ownerId = Guid.NewGuid();
        var alias = $"it-{Guid.NewGuid():N}"[..32];
        var clientId = $"it-client-{Guid.NewGuid():N}"[..40];
        var connectionId = $"it-connection-{Guid.NewGuid():N}"[..40];
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using (var schemaDb = new ConnectorDbContext(options))
        {
            await schemaDb.ApplyVersionedSchemaAsync();
            schemaDb.UserAccounts.Add(new UserAccountEntity
            {
                Id = ownerId,
                Name = "Integration Owner",
                Email = $"{ownerId:N}@integration.test",
                PasswordHash = "not-a-real-password",
                ConnectorClientId = clientId,
            });
            schemaDb.ConnectorClients.Add(new ConnectorClientCredentialEntity
            {
                Id = connectionId,
                ClientId = clientId,
                MoodleAlias = alias,
                MoodleBaseUrl = "https://integration.example",
                MoodleUsernameEncrypted = "encrypted-user",
                MoodlePasswordEncrypted = "encrypted-password",
                MoodleTarget = alias,
                IsActive = true,
            });
            await schemaDb.SaveChangesAsync();
        }

        async Task SaveAsync(string state)
        {
            await using var db = new ConnectorDbContext(options);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            using var metrics = new MoodleSnapshotMetrics();
            var store = new MoodleSnapshotStore(db, cache, metrics, NullLogger<MoodleSnapshotStore>.Instance);
            await store.SaveAsync(
                ownerId,
                alias,
                "courses",
                string.Empty,
                new Dictionary<string, string> { ["state"] = state },
                "warm",
                frozen: false,
                complete: true,
                recordCount: 1,
                DateTimeOffset.UtcNow);
        }

        await Task.WhenAll(SaveAsync("first"), SaveAsync("second"));

        await using var verifyDb = new ConnectorDbContext(options);
        var heads = await verifyDb.MoodleSnapshots
            .Where(item => item.OwnerId == ownerId && item.ConnectionId == connectionId)
            .ToListAsync();
        var head = Assert.Single(heads);
        Assert.True(
            head.PayloadJson.Contains("\"first\"", StringComparison.Ordinal) ||
            head.PayloadJson.Contains("\"second\"", StringComparison.Ordinal));
        Assert.Equal(alias, head.ConnectionAlias);
    }
}
