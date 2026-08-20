using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleSnapshotSyncQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<MoodleSnapshotSyncQueue> logger) : BackgroundService, IMoodleSnapshotSyncQueue
{
    private readonly Channel<MoodleSnapshotSyncRequest> _queue = Channel.CreateUnbounded<MoodleSnapshotSyncRequest>();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool Enqueue(MoodleSnapshotSyncRequest request)
    {
        var key = $"{request.OwnerId}:{request.ConnectionAlias}";
        lock (_gate)
        {
            if (!request.Force && !_pending.Add(key)) return false;
            _pending.Add(key);
        }
        return _queue.Writer.TryWrite(request);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var key = $"{request.OwnerId}:{request.ConnectionAlias}";
            try
            {
                using var scope = scopeFactory.CreateScope();
                var executionContext = scope.ServiceProvider.GetRequiredService<IConnectorExecutionContext>();
                var selection = scope.ServiceProvider.GetRequiredService<IMoodleConnectionSelection>();
                executionContext.Enter(request.ClientId, request.OwnerId.ToString(), null);
                selection.Alias = request.ConnectionAlias;
                await SyncAsync(scope.ServiceProvider, request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Moodle snapshot sync failed for connection {ConnectionAlias}", request.ConnectionAlias);
            }
            finally
            {
                lock (_gate) _pending.Remove(key);
            }
        }
    }

    private static async Task SyncAsync(IServiceProvider services, MoodleSnapshotSyncRequest request, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ConnectorDbContext>();
        var coursesGateway = services.GetRequiredService<IMoodleCoursesGateway>();
        var contentsGateway = services.GetRequiredService<IMoodleCourseContentsGateway>();
        var participantsGateway = services.GetRequiredService<IMoodleParticipantsGateway>();
        var now = DateTimeOffset.UtcNow;

        var courses = new List<CourseSummary>();
        for (var page = 1; page <= 10; page++)
        {
            var result = await coursesGateway.GetMyCoursesAsync(request.UserExternalId, 100, page, cancellationToken);
            courses.AddRange(result.Items);
            if (!result.HasNextPage) break;
        }

        await SaveAsync(db, request, "courses", string.Empty, courses, "warm", false, now, cancellationToken);
        foreach (var course in courses)
        {
            var finished = course.EndDate is not null && course.EndDate < now;
            var existing = await db.Set<MoodleSnapshotEntity>().AsNoTracking().SingleOrDefaultAsync(item => item.OwnerId == request.OwnerId && item.ConnectionAlias == request.ConnectionAlias && item.SnapshotType == "activities" && item.CourseId == course.CourseId, cancellationToken);
            if (finished && existing?.IsFrozen == true && !request.Force) continue;

            var tier = finished ? "cold" : "hot";
            if (finished && !request.Force) continue;

            var contents = await contentsGateway.GetCourseContentsAsync(request.UserExternalId, course.CourseId, CourseActivityModuleTypes.All, false, false, cancellationToken);
            await SaveAsync(db, request, "activities", course.CourseId, contents, tier, finished, now, cancellationToken);
            if (!finished)
            {
                var students = await participantsGateway.GetCourseParticipantsAsync(request.UserExternalId, course.CourseId, ParticipantStatusFilter.Active, 1, 1000, true, true, null, cancellationToken);
                await SaveAsync(db, request, "students", course.CourseId, students, "warm", false, now, cancellationToken);
                var groups = await participantsGateway.GetCourseGroupsAsync(request.UserExternalId, course.CourseId, cancellationToken);
                await SaveAsync(db, request, "groups", course.CourseId, groups, "warm", false, now, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SaveAsync<T>(ConnectorDbContext db, MoodleSnapshotSyncRequest request, string type, string courseId, T payload, string tier, bool frozen, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entity = await db.Set<MoodleSnapshotEntity>().SingleOrDefaultAsync(item => item.OwnerId == request.OwnerId && item.ConnectionAlias == request.ConnectionAlias && item.SnapshotType == type && item.CourseId == courseId, cancellationToken);
        if (entity is null)
        {
            entity = new MoodleSnapshotEntity { Id = Guid.NewGuid(), OwnerId = request.OwnerId, ConnectionAlias = request.ConnectionAlias, SnapshotType = type, CourseId = courseId };
            db.Add(entity);
        }
        entity.PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        entity.Tier = tier;
        entity.IsFrozen = frozen;
        entity.UpdatedAt = now;
    }
}
