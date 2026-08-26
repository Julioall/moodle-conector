using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleSnapshotStore(
    ConnectorDbContext dbContext,
    IMemoryCache memoryCache,
    MoodleSnapshotMetrics metrics,
    ILogger<MoodleSnapshotStore> logger) : IMoodleSnapshotStore
{
    private static readonly TimeSpan HotTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WarmTtl = TimeSpan.FromHours(2);
    private static readonly TimeSpan L1Duration = TimeSpan.FromSeconds(15);
    private static readonly ConcurrentDictionary<string, long> CacheVersions = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<MoodleSnapshotEnvelope<IReadOnlyList<CourseSummary>>?> GetCoursesAsync(Guid ownerId, string connectionAlias, CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<CourseSummary>>(ownerId, connectionAlias, "courses", string.Empty, cancellationToken);

    public Task<MoodleSnapshotEnvelope<CourseContentsSummary>?> GetActivitiesAsync(Guid ownerId, string connectionAlias, string courseId, CancellationToken cancellationToken = default) =>
        ReadAsync<CourseContentsSummary>(ownerId, connectionAlias, "activities", courseId, cancellationToken);

    public Task<MoodleSnapshotEnvelope<CourseParticipantsPage>?> GetStudentsAsync(Guid ownerId, string connectionAlias, string courseId, CancellationToken cancellationToken = default) =>
        ReadAsync<CourseParticipantsPage>(ownerId, connectionAlias, "students", courseId, cancellationToken);

    public Task<MoodleSnapshotEnvelope<IReadOnlyList<CourseGroupSummary>>?> GetGroupsAsync(Guid ownerId, string connectionAlias, string courseId, CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<CourseGroupSummary>>(ownerId, connectionAlias, "groups", courseId, cancellationToken);

    public Task<MoodleSnapshotEnvelope<T>?> GetAsync<T>(Guid ownerId, string connectionAlias, string dataset, string courseId = "", CancellationToken cancellationToken = default) =>
        ReadAsync<T>(ownerId, connectionAlias, dataset, courseId, cancellationToken);

    public async Task SaveAsync<T>(
        Guid ownerId,
        string connectionAlias,
        string dataset,
        string courseId,
        T payload,
        string tier,
        bool frozen,
        bool complete,
        int recordCount,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var normalizedDataset = Normalize(dataset);
        var normalizedCourseId = courseId?.Trim() ?? string.Empty;
        var connectionId = await MoodleConnectionIdentity.ResolveAsync(
            dbContext, ownerId, string.Empty, connectionAlias, cancellationToken);
        var entity = await dbContext.MoodleSnapshots.SingleOrDefaultAsync(
            item => item.OwnerId == ownerId && item.ConnectionId == connectionId && item.SnapshotType == normalizedDataset && item.CourseId == normalizedCourseId,
            cancellationToken);
        entity ??= await dbContext.MoodleSnapshots.SingleOrDefaultAsync(
            item => item.OwnerId == ownerId && item.ConnectionId == string.Empty && item.ConnectionAlias == connectionAlias && item.SnapshotType == normalizedDataset && item.CourseId == normalizedCourseId,
            cancellationToken);
        var freshUntil = now.Add(GetFreshTtl(normalizedDataset, tier, frozen));
        var staleUntil = freshUntil.Add(GetStaleWindow(normalizedDataset, frozen));
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        if (entity is null)
        {
            entity = new MoodleSnapshotEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                ConnectionId = connectionId,
                ConnectionAlias = connectionAlias,
                SnapshotType = normalizedDataset,
                CourseId = normalizedCourseId,
            };
            dbContext.MoodleSnapshots.Add(entity);
        }

        entity.ConnectionId = connectionId;
        entity.ConnectionAlias = connectionAlias;
        entity.PayloadJson = payloadJson;
        entity.Tier = tier;
        entity.IsFrozen = frozen;
        entity.UpdatedAt = now;
        entity.FreshUntil = freshUntil;
        entity.StaleUntil = staleUntil;
        entity.LastError = null;
        entity.PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();
        entity.IsComplete = complete;
        entity.RecordCount = recordCount;
        await dbContext.SaveChangesAsync(cancellationToken);
        Invalidate(ownerId, connectionAlias, normalizedDataset, normalizedCourseId);
        metrics.RecordRefresh(normalizedDataset);
    }

    public void Invalidate(Guid ownerId, string connectionAlias, string dataset, string courseId = "")
    {
        var normalizedAlias = connectionAlias.Trim().ToLowerInvariant();
        var scopeKey = $"{ownerId:N}:{normalizedAlias}";
        CacheVersions.AddOrUpdate(scopeKey, 1, static (_, version) => version + 1);
        memoryCache.Remove(CacheKey(ownerId, LegacyConnectionId(ownerId, normalizedAlias), normalizedAlias, Normalize(dataset), courseId?.Trim() ?? string.Empty));
    }

    private async Task<MoodleSnapshotEnvelope<T>?> ReadAsync<T>(Guid ownerId, string connectionAlias, string type, string courseId, CancellationToken cancellationToken)
    {
        type = Normalize(type);
        courseId = courseId?.Trim() ?? string.Empty;
        var connectionId = await MoodleConnectionIdentity.ResolveAsync(
            dbContext, ownerId, string.Empty, connectionAlias, cancellationToken);
        var key = CacheKey(ownerId, connectionId, connectionAlias, type, courseId);
        if (memoryCache.TryGetValue(key, out MoodleSnapshotEnvelope<T>? cached))
        {
            metrics.RecordL1Hit(type);
            return cached;
        }

        var entity = await dbContext.Set<MoodleSnapshotEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId && item.ConnectionId == connectionId && item.SnapshotType == type && item.CourseId == courseId, cancellationToken);
        entity ??= await dbContext.Set<MoodleSnapshotEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId && item.ConnectionId == string.Empty && item.ConnectionAlias == connectionAlias && item.SnapshotType == type && item.CourseId == courseId, cancellationToken);
        if (entity is null)
        {
            metrics.RecordMiss(type);
            return null;
        }
        try
        {
            var data = JsonSerializer.Deserialize<T>(entity.PayloadJson, JsonOptions);
            if (data is null) return null;
            var now = DateTimeOffset.UtcNow;
            var freshUntil = entity.FreshUntil ?? entity.UpdatedAt.Add(
                entity.Tier.Equals("hot", StringComparison.OrdinalIgnoreCase) ? HotTtl : WarmTtl);
            var staleUntil = entity.StaleUntil ?? freshUntil.Add(GetStaleWindow(type, entity.IsFrozen));
            if (!entity.IsFrozen && now > staleUntil)
            {
                metrics.RecordMiss(type);
                return null;
            }
            var envelope = new MoodleSnapshotEnvelope<T>(
                data,
                entity.UpdatedAt,
                !entity.IsFrozen && now >= freshUntil,
                entity.IsFrozen,
                entity.Tier,
                freshUntil,
                staleUntil,
                entity.LastAttemptAt,
                entity.LastError,
                entity.IsComplete,
                entity.RecordCount);
            memoryCache.Set(key, envelope, L1Duration);
            metrics.RecordL2Hit(type, entity.UpdatedAt);
            return envelope;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Snapshot JSON inválido. Dataset={Dataset} CourseId={CourseId}", type, courseId);
            return null;
        }
    }

    private static string CacheKey(Guid ownerId, string connectionId, string connectionAlias, string dataset, string courseId)
    {
        var version = CacheVersions.GetValueOrDefault($"{ownerId:N}:{connectionAlias.Trim().ToLowerInvariant()}");
        return $"moodle-snapshot:{ownerId}:{connectionId}:{dataset}:{courseId}:v{version}";
    }

    private static string LegacyConnectionId(Guid ownerId, string alias) =>
        $"legacy-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"legacy:{ownerId:N}:{alias}"))).ToLowerInvariant()}";

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static TimeSpan GetFreshTtl(string dataset, string tier, bool frozen) =>
        frozen ? TimeSpan.FromDays(3650) : dataset switch
        {
            MoodleSnapshotDatasets.Courses => TimeSpan.FromDays(2),
            MoodleSnapshotDatasets.Activities => TimeSpan.FromHours(24),
            MoodleSnapshotDatasets.Students or MoodleSnapshotDatasets.Groups => tier.Equals("hot", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromHours(1) : TimeSpan.FromHours(4),
            MoodleSnapshotDatasets.Submissions => TimeSpan.FromMinutes(15),
            MoodleSnapshotDatasets.DashboardPending => TimeSpan.FromHours(24),
            MoodleSnapshotDatasets.DashboardAccess => TimeSpan.FromMinutes(45),
            _ => tier.Equals("hot", StringComparison.OrdinalIgnoreCase) ? HotTtl : WarmTtl,
        };

    private static TimeSpan GetStaleWindow(string dataset, bool frozen) =>
        frozen ? TimeSpan.FromDays(3650) : dataset switch
        {
            MoodleSnapshotDatasets.Courses => TimeSpan.FromDays(7),
            MoodleSnapshotDatasets.Activities => TimeSpan.FromDays(3),
            MoodleSnapshotDatasets.Students or MoodleSnapshotDatasets.Groups => TimeSpan.FromHours(24),
            MoodleSnapshotDatasets.Submissions => TimeSpan.FromHours(6),
            MoodleSnapshotDatasets.DashboardPending => TimeSpan.FromDays(3),
            MoodleSnapshotDatasets.DashboardAccess => TimeSpan.FromHours(6),
            _ => TimeSpan.FromHours(12),
        };
}
