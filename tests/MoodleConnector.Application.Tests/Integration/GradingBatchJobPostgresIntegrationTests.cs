using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Grading;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Integration;

public sealed class GradingBatchJobPostgresIntegrationTests
{
    [Fact]
    public async Task ConcurrentClaims_OnlyOneWorkerAcquiresLease_AndExpiredLeaseIsRecoverable()
    {
        var connectionString = Environment.GetEnvironmentVariable("MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var schemaDb = new ConnectorDbContext(options))
        {
            await schemaDb.ApplyVersionedSchemaAsync();
        }

        var batch = AssistedGradingBatch.Create(
            courseId: 10,
            assignmentIds: [501],
            createdBySubject: $"integration-{Guid.NewGuid():N}",
            createdByMoodleUserId: 321,
            totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await using (var seedDb = new ConnectorDbContext(options))
        {
            seedDb.GradingBatches.Add(batch);
            seedDb.GradingItems.Add(item);
            await seedDb.SaveChangesAsync();
        }

        var claimTime = DateTimeOffset.UtcNow;
        async Task<GradingBatchLeaseClaim?> ClaimAsync(string workerId)
        {
            await using var db = new ConnectorDbContext(options);
            var store = new GradingReviewRepository(db);
            return await store.TryClaimBatchAsync(
                batch.Id,
                workerId,
                claimTime,
                TimeSpan.FromMinutes(10),
                CancellationToken.None);
        }

        var claims = await Task.WhenAll(ClaimAsync("worker-a"), ClaimAsync("worker-b"));
        var winner = Assert.Single(claims, claim => claim is not null)!;
        Assert.Single(claims, claim => claim is null);
        await using (var verifyDb = new ConnectorDbContext(options))
        {
            var persisted = await verifyDb.GradingBatches.SingleAsync(item => item.Id == batch.Id);
            Assert.Equal(winner.WorkerId, persisted.LeaseOwner);
            Assert.Equal(1, persisted.AttemptCount);
        }

        await using (var expiredDb = new ConnectorDbContext(options))
        {
            var store = new GradingReviewRepository(expiredDb);
            var recovered = await store.RecoverExpiredBatchLeasesAsync(
                claimTime.AddMinutes(11),
                CancellationToken.None);
            Assert.Equal(1, recovered);
        }

        await using (var recoveredDb = new ConnectorDbContext(options))
        {
            var store = new GradingReviewRepository(recoveredDb);
            var retry = await store.TryClaimBatchAsync(
                batch.Id,
                "worker-c",
                claimTime.AddMinutes(11),
                TimeSpan.FromMinutes(10),
                CancellationToken.None);
            Assert.NotNull(retry);
            Assert.Equal(2, retry!.AttemptCount);
        }
    }

    [Fact]
    public async Task ConcurrentItemClaims_OnlyOneWorkerAcquiresLease_AndRetryCountsAttempt()
    {
        var connectionString = Environment.GetEnvironmentVariable("MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var schemaDb = new ConnectorDbContext(options))
        {
            await schemaDb.ApplyVersionedSchemaAsync();
        }

        var batch = AssistedGradingBatch.Create(
            courseId: 10,
            assignmentIds: [501],
            createdBySubject: $"integration-item-{Guid.NewGuid():N}",
            createdByMoodleUserId: 321,
            totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await using (var seedDb = new ConnectorDbContext(options))
        {
            seedDb.GradingBatches.Add(batch);
            seedDb.GradingItems.Add(item);
            await seedDb.SaveChangesAsync();
        }

        var claimTime = DateTimeOffset.UtcNow;
        async Task<GradingItemLeaseClaim?> ClaimAsync(string workerId)
        {
            await using var db = new ConnectorDbContext(options);
            var store = new GradingReviewRepository(db);
            return await store.TryClaimItemAsync(
                batch.Id,
                item.Id,
                workerId,
                claimTime,
                TimeSpan.FromMinutes(10),
                CancellationToken.None);
        }

        var claims = await Task.WhenAll(ClaimAsync("item-worker-a"), ClaimAsync("item-worker-b"));
        var winner = Assert.Single(claims, claim => claim is not null)!;
        Assert.Single(claims, claim => claim is null);
        Assert.Equal(1, winner.AttemptCount);

        await using (var expiredDb = new ConnectorDbContext(options))
        {
            var store = new GradingReviewRepository(expiredDb);
            var recovered = await store.RecoverExpiredItemLeasesAsync(
                claimTime.AddMinutes(11),
                CancellationToken.None);
            Assert.Equal(1, recovered);
        }

        await using (var retryDb = new ConnectorDbContext(options))
        {
            var store = new GradingReviewRepository(retryDb);
            var retry = await store.TryClaimItemAsync(
                batch.Id,
                item.Id,
                "item-worker-c",
                claimTime.AddMinutes(11),
                TimeSpan.FromMinutes(10),
                CancellationToken.None);
            Assert.NotNull(retry);
            Assert.Equal(2, retry!.AttemptCount);
        }
    }

    [Fact]
    public async Task VersionedProposal_PersistsJsonbAndIsIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable("MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var schemaDb = new ConnectorDbContext(options))
        {
            await schemaDb.ApplyVersionedSchemaAsync();
        }

        var batch = AssistedGradingBatch.Create(10, [501], $"integration-proposal-{Guid.NewGuid():N}", 321, 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        await using (var seedDb = new ConnectorDbContext(options))
        {
            seedDb.GradingBatches.Add(batch);
            seedDb.GradingItems.Add(item);
            await seedDb.SaveChangesAsync();
        }

        await using (var proposalDb = new ConnectorDbContext(options))
        {
            var store = new GradingReviewRepository(proposalDb);
            var proposal = AiGradingProposal.Create(
                item.Id,
                batch.Id,
                await store.GetNextVersionAsync(item.Id, CancellationToken.None),
                null,
                8m,
                "Feedback",
                [new AiGradingCriterionProposal("C1", "Criterio", 10m, 8m, AiGradingCriterionSource.FormalRubric, "evidencia", null, false, false, [])],
                [],
                [],
                new GradingScaleSnapshot(10m, "points", "Moodle"),
                new GradingExtractionSummary("succeeded", 1, false, 10, 10, null),
                new GradingEvidenceCoverage(1, 1, 1, 1, 10, 10, false),
                new AiGradingConfidenceResult(.9m, [], false),
                reviewRequired: true);
            await store.PublishAsync(proposal, CancellationToken.None);
            await store.PublishAsync(proposal, CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var verifyDb = new ConnectorDbContext(options))
        {
            var documents = await verifyDb.AiGradingProposals
                .Where(proposal => proposal.GradingItemId == item.Id)
                .ToArrayAsync();
            var document = Assert.Single(documents);
            Assert.Equal("1", document.SchemaVersion);
            Assert.Matches("^[0-9a-f]{64}$", document.ProposalHash);
            Assert.Contains("C1", document.PayloadJson, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DeferredArtifactSource_PersistsAndCanBeMaterializedWithoutToken()
    {
        var connectionString = Environment.GetEnvironmentVariable("MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var schemaDb = new ConnectorDbContext(options))
        {
            await schemaDb.ApplyVersionedSchemaAsync();
        }

        var batch = AssistedGradingBatch.Create(10, [501], $"integration-artifact-{Guid.NewGuid():N}", 321, 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        var artifact = new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "submission_file",
            "entrega.txt",
            "text/plain",
            Sha256: null,
            SizeBytes: 31,
            ExtractionStatus.Pending,
            ExtractedTextRef: null,
            SummaryRef: "pending_ingestion",
            DateTimeOffset.UtcNow,
            "https://moodle.example/pluginfile.php/entrega.txt");

        await using (var seedDb = new ConnectorDbContext(options))
        {
            seedDb.GradingBatches.Add(batch);
            seedDb.GradingItems.Add(item);
            seedDb.GradingArtifacts.Add(artifact);
            await seedDb.SaveChangesAsync();
        }

        await using (var updateDb = new ConnectorDbContext(options))
        {
            var store = new GradingReviewRepository(updateDb);
            var persisted = Assert.Single(await store.ListArtifactsByItemAsync(item.Id, CancellationToken.None));
            Assert.Equal("https://moodle.example/pluginfile.php/entrega.txt", persisted.SourceUrl);

            await store.UpdateArtifactAsync(
                persisted with
                {
                    ExtractionStatus = ExtractionStatus.Succeeded,
                    ExtractedTextRef = "texto extraido",
                    SummaryRef = null,
                    SourceUrl = null
                },
                CancellationToken.None);
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var verifyDb = new ConnectorDbContext(options))
        {
            var persisted = Assert.Single(await verifyDb.GradingArtifacts
                .Where(candidate => candidate.Id == artifact.Id)
                .ToArrayAsync());
            Assert.Equal(ExtractionStatus.Succeeded, persisted.ExtractionStatus);
            Assert.Equal("texto extraido", persisted.ExtractedTextRef);
            Assert.Null(persisted.SourceUrl);
        }
    }
}
