using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleSnapshotStore(ConnectorDbContext dbContext) : IMoodleSnapshotStore
{
    private static readonly TimeSpan HotTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WarmTtl = TimeSpan.FromHours(2);
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

    private async Task<MoodleSnapshotEnvelope<T>?> ReadAsync<T>(Guid ownerId, string connectionAlias, string type, string courseId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Set<MoodleSnapshotEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId && item.ConnectionAlias == connectionAlias && item.SnapshotType == type && item.CourseId == courseId, cancellationToken);
        if (entity is null) return null;
        try
        {
            var data = JsonSerializer.Deserialize<T>(entity.PayloadJson, JsonOptions);
            if (data is null) return null;
            var ttl = entity.Tier.Equals("hot", StringComparison.OrdinalIgnoreCase) ? HotTtl : WarmTtl;
            return new MoodleSnapshotEnvelope<T>(data, entity.UpdatedAt, !entity.IsFrozen && DateTimeOffset.UtcNow - entity.UpdatedAt > ttl, entity.IsFrozen, entity.Tier);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
