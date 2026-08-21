using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    ILogger<MoodleSnapshotSyncQueue> logger,
    MoodleSnapshotMetrics metrics) : BackgroundService, IMoodleSnapshotSyncQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // The channel is only an in-process accelerator. Durable state is stored in
    // moodle_sync_states and is recovered by the polling loop after a restart.
    private readonly Channel<MoodleSnapshotSyncRequest> _queue =
        Channel.CreateBounded<MoodleSnapshotSyncRequest>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ConnectionLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTimeOffset _applicationStartedAt = DateTimeOffset.UtcNow;

    public bool Enqueue(MoodleSnapshotSyncRequest request) =>
        EnqueueAsync(request).GetAwaiter().GetResult();

    public async Task<bool> EnqueueAsync(
        MoodleSnapshotSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        request = Normalize(request);
        var now = DateTimeOffset.UtcNow;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var state = await db.MoodleSyncStates.SingleOrDefaultAsync(
            item => item.OwnerId == request.OwnerId &&
                    item.ConnectionAlias == request.ConnectionAlias &&
                    item.Dataset == request.Dataset &&
                    item.CourseId == (request.CourseId ?? string.Empty),
            cancellationToken);

        if (state is not null && MoodleSyncLeasePolicy.IsActive(state, now))
        {
            // A running job already owns the key. A subsequent force refresh is
            // represented by the next scheduled attempt instead of duplicating
            // the expensive Moodle fan-out.
            if (request.Force)
            {
                state.ForceRequested = true;
                state.NextSyncAt = now;
                state.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }

            return true;
        }

        if (state is null)
        {
            state = new MoodleSyncStateEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = request.OwnerId,
                ConnectionAlias = request.ConnectionAlias,
                Dataset = request.Dataset,
                CourseId = request.CourseId ?? string.Empty,
            };
            db.MoodleSyncStates.Add(state);
        }
        else if (!request.Force && state.NextSyncAt is { } next && next > now)
        {
            return false;
        }

        state.ClientId = request.ClientId;
        state.UserExternalId = request.UserExternalId;
        state.Priority = Math.Clamp(request.Priority, 0, 1000);
        state.Status = "pending";
        state.NextSyncAt = now;
        state.LastError = null;
        state.ForceRequested |= request.Force;
        state.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return TryEnqueueSignal(request);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MoodleSnapshotSyncQueue iniciada.");

        try
        {
            await RecoverOrphanedStatesAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                while (_queue.Reader.TryRead(out var request))
                {
                    await ProcessRequestAsync(request, stoppingToken);
                }

                await EnqueueDueStatesAsync(stoppingToken);

                var signal = _queue.Reader.WaitToReadAsync(stoppingToken).AsTask();
                var tick = Task.Delay(PollInterval, stoppingToken);
                await Task.WhenAny(signal, tick);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("MoodleSnapshotSyncQueue encerrada.");
    }

    private async Task EnqueueDueStatesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var now = DateTimeOffset.UtcNow;
        var states = await db.MoodleSyncStates
            .AsNoTracking()
            .Where(item => item.ClientId != string.Empty &&
                          item.NextSyncAt != null &&
                          item.NextSyncAt <= now &&
                          (item.Status != "running" || item.LeaseUntil == null || item.LeaseUntil <= now))
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.NextSyncAt)
            .Take(32)
            .ToArrayAsync(cancellationToken);

        foreach (var state in states)
        {
            TryEnqueueSignal(new MoodleSnapshotSyncRequest(
                state.OwnerId,
                state.ClientId,
                state.ConnectionAlias,
                state.UserExternalId,
                state.ForceRequested,
                state.Dataset,
                string.IsNullOrWhiteSpace(state.CourseId) ? null : state.CourseId,
                state.Priority));
        }
    }

    private async Task RecoverOrphanedStatesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var now = DateTimeOffset.UtcNow;
        var orphanedStates = await db.MoodleSyncStates
            .Where(item => item.Dataset != MoodleSnapshotDatasets.DashboardPending &&
                           item.Status == "running" &&
                           (item.LastStartedAt == null || item.LastStartedAt < _applicationStartedAt))
            .ToListAsync(cancellationToken);

        foreach (var orphanedState in orphanedStates)
        {
            orphanedState.Status = "pending";
            orphanedState.LeaseUntil = null;
            orphanedState.NextSyncAt = now;
            orphanedState.LastError = null;
            orphanedState.UpdatedAt = now;
        }

        var recovered = orphanedStates.Count;
        if (recovered > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (recovered > 0)
        {
            logger.LogWarning(
                "Estados de sincronização órfãos recuperados após reinicialização. Count={Count}",
                recovered);
        }
    }

    private async Task ProcessRequestAsync(
        MoodleSnapshotSyncRequest request,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(request);
        CancellationTokenSource? leaseHeartbeatCancellation = null;
        Task? leaseHeartbeat = null;
        try
        {
            var work = await TryClaimAsync(request, cancellationToken);
            if (work is null)
            {
                return;
            }

            leaseHeartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            leaseHeartbeat = RenewLeaseAsync(work.StateId, leaseHeartbeatCancellation.Token);

            using var scope = scopeFactory.CreateScope();
            var executionContext = scope.ServiceProvider.GetRequiredService<IConnectorExecutionContext>();
            var selection = scope.ServiceProvider.GetRequiredService<IMoodleConnectionSelection>();
            executionContext.Enter(work.ClientId, work.OwnerId.ToString(), null);
            selection.Alias = work.ConnectionAlias;

            var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
            var limiter = ConnectionLocks.GetOrAdd(
                $"{work.OwnerId}:{work.ConnectionAlias}",
                static _ => new SemaphoreSlim(1, 1));
            await limiter.WaitAsync(cancellationToken);
            var startedAt = Stopwatch.GetTimestamp();
            int records;
            try
            {
                records = await SyncAsync(scope.ServiceProvider, work, cancellationToken);
                var snapshotStore = scope.ServiceProvider.GetRequiredService<IMoodleSnapshotStore>();
                var invalidatedDataset = work.Dataset == MoodleSnapshotDatasets.Connection
                    ? MoodleSnapshotDatasets.Courses
                    : work.Dataset;
                snapshotStore.Invalidate(
                    work.OwnerId,
                    work.ConnectionAlias,
                    invalidatedDataset,
                    work.CourseId ?? string.Empty);
                metrics.RecordRefresh(invalidatedDataset);
            }
            finally
            {
                limiter.Release();
                metrics.RecordSyncDuration(work.Dataset, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
            var state = await db.MoodleSyncStates.SingleAsync(item => item.Id == work.StateId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            state.Status = "completed";
            state.LastCompletedAt = now;
            state.NextSyncAt = GetNextAutomaticSyncAt(work.Dataset, now);
            state.LastError = null;
            state.LeaseUntil = null;
            state.RecordsSynced = records;
            state.ForceRequested = false;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Moodle snapshot sync completed. OwnerId={OwnerId} Connection={ConnectionAlias} Dataset={Dataset} CourseId={CourseId} Records={Records}",
                work.OwnerId,
                work.ConnectionAlias,
                work.Dataset,
                work.CourseId,
                records);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(request, exception, cancellationToken);
            logger.LogWarning(
                exception,
                "Moodle snapshot sync failed. OwnerId={OwnerId} Connection={ConnectionAlias} Dataset={Dataset} CourseId={CourseId}",
                request.OwnerId,
                request.ConnectionAlias,
                request.Dataset,
                request.CourseId);
        }
        finally
        {
            if (leaseHeartbeatCancellation is not null)
            {
                leaseHeartbeatCancellation.Cancel();
                if (leaseHeartbeat is not null)
                {
                    try
                    {
                        await leaseHeartbeat;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                leaseHeartbeatCancellation.Dispose();
            }

            lock (_gate)
            {
                _pending.Remove(key);
            }
        }
    }

    private async Task RenewLeaseAsync(Guid stateId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
                await db.MoodleSyncStates
                    .Where(item => item.Id == stateId && item.Status == "running")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(30))
                        .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Não foi possível renovar o lease da sincronização Moodle. StateId={StateId}", stateId);
        }
    }

    private async Task<SyncWork?> TryClaimAsync(
        MoodleSnapshotSyncRequest request,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var state = await db.MoodleSyncStates.AsNoTracking().SingleOrDefaultAsync(
            item => item.OwnerId == request.OwnerId &&
                    item.ConnectionAlias == request.ConnectionAlias &&
                    item.Dataset == request.Dataset &&
                    item.CourseId == (request.CourseId ?? string.Empty),
            cancellationToken);
        if (state is null || state.NextSyncAt is not { } next || next > DateTimeOffset.UtcNow)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (MoodleSyncLeasePolicy.IsActive(state, now))
        {
            return null;
        }

        var attemptCount = state.AttemptCount + 1;
        var force = request.Force || state.ForceRequested;
        var updated = await db.MoodleSyncStates
            .Where(item => item.Id == state.Id &&
                           (item.Status != "running" || item.LeaseUntil == null || item.LeaseUntil <= now) &&
                           (item.LeaseUntil == null || item.LeaseUntil <= now) &&
                           item.NextSyncAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "running")
                .SetProperty(item => item.LastStartedAt, now)
                .SetProperty(item => item.LastAttemptAt, now)
                .SetProperty(item => item.LeaseUntil, now.AddMinutes(30))
                .SetProperty(item => item.AttemptCount, attemptCount)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        if (updated != 1)
        {
            return null;
        }

        return new SyncWork(
            state.Id,
            state.OwnerId,
            state.ClientId,
            state.ConnectionAlias,
            state.UserExternalId,
            state.Dataset,
            string.IsNullOrWhiteSpace(state.CourseId) ? null : state.CourseId,
            force,
            attemptCount);
    }

    private async Task MarkFailedAsync(
        MoodleSnapshotSyncRequest request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
            var state = await db.MoodleSyncStates.SingleOrDefaultAsync(
                item => item.OwnerId == request.OwnerId &&
                        item.ConnectionAlias == request.ConnectionAlias &&
                        item.Dataset == request.Dataset &&
                        item.CourseId == (request.CourseId ?? string.Empty),
                cancellationToken);
            if (state is null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var seconds = Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, state.AttemptCount - 1))) * (0.75 + Random.Shared.NextDouble() * 0.5);
            state.Status = "failed";
            state.LastError = exception.Message.Length > 4000 ? exception.Message[..4000] : exception.Message;
            state.NextSyncAt = now.AddSeconds(seconds);
            state.LeaseUntil = null;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception persistenceException)
        {
            logger.LogError(persistenceException, "Falha ao persistir o estado de erro da sincronização Moodle.");
        }
    }

    private static async Task<int> SyncAsync(
        IServiceProvider services,
        SyncWork work,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ConnectorDbContext>();
        var coursesGateway = services.GetRequiredService<IMoodleCoursesGateway>();
        var contentsGateway = services.GetRequiredService<IMoodleCourseContentsGateway>();
        var participantsGateway = services.GetRequiredService<IMoodleParticipantsGateway>();

        if (work.Dataset is MoodleSnapshotDatasets.Connection or MoodleSnapshotDatasets.Courses)
        {
            var courses = await ReadCoursesAsync(coursesGateway, work.UserExternalId, cancellationToken);
            await SaveAsync(db, work, MoodleSnapshotDatasets.Courses, string.Empty, courses, "warm", false, cancellationToken);

            if (work.Dataset == MoodleSnapshotDatasets.Connection)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var course in courses)
                {
                    var finished = course.EndDate is not null && course.EndDate < now;
                    var activityNext = GetNextAutomaticSyncAt(MoodleSnapshotDatasets.Activities, now);
                    await EnsureStateAsync(db, work, MoodleSnapshotDatasets.Activities, course.CourseId, activityNext, 20, cancellationToken);
                    await EnsureStateAsync(db, work, MoodleSnapshotDatasets.Students, course.CourseId, finished ? now.AddDays(7) : now, 30, cancellationToken);
                    await EnsureStateAsync(db, work, MoodleSnapshotDatasets.Groups, course.CourseId, finished ? now.AddDays(7) : now, 60, cancellationToken);
                }

                var courseIds = courses.Select(course => course.CourseId).ToArray();
                var cutoff = now.AddDays(-7);
                await db.MoodleSnapshots
                    .Where(item => item.OwnerId == work.OwnerId &&
                                   item.ConnectionAlias == work.ConnectionAlias &&
                                   item.CourseId != string.Empty &&
                                   !courseIds.Contains(item.CourseId) &&
                                   item.UpdatedAt < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);
                await db.MoodleSyncStates
                    .Where(item => item.OwnerId == work.OwnerId &&
                                   item.ConnectionAlias == work.ConnectionAlias &&
                                   item.CourseId != string.Empty &&
                                   !courseIds.Contains(item.CourseId) &&
                                   item.UpdatedAt < cutoff)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            return courses.Count;
        }

        if (string.IsNullOrWhiteSpace(work.CourseId))
        {
            return 0;
        }

        var courseSummary = await coursesGateway.GetMyCourseAsync(work.UserExternalId, work.CourseId, cancellationToken);
        if (courseSummary is null)
        {
            return 0;
        }

        var nowForCourse = DateTimeOffset.UtcNow;
        var finishedCourse = courseSummary.EndDate is not null && courseSummary.EndDate < nowForCourse;
        var existingSnapshot = await db.MoodleSnapshots.AsNoTracking().SingleOrDefaultAsync(
            item => item.OwnerId == work.OwnerId &&
                    item.ConnectionAlias == work.ConnectionAlias &&
                    item.SnapshotType == MoodleSnapshotDatasets.Activities &&
                    item.CourseId == courseSummary.CourseId,
            cancellationToken);
        if (finishedCourse && existingSnapshot?.IsFrozen == true && !work.Force)
        {
            return 0;
        }

        switch (work.Dataset)
        {
            case MoodleSnapshotDatasets.Activities:
            {
                var contents = await contentsGateway.GetCourseContentsAsync(
                    work.UserExternalId,
                    courseSummary.CourseId,
                    CourseActivityModuleTypes.All,
                    includeHidden: false,
                    onlyWithFiles: false,
                    cancellationToken);
                await SaveAsync(db, work, MoodleSnapshotDatasets.Activities, courseSummary.CourseId, contents, finishedCourse ? "cold" : "hot", finishedCourse, cancellationToken);
                break;
            }
            case MoodleSnapshotDatasets.Students:
            {
                var students = await participantsGateway.GetCourseParticipantsAsync(
                    work.UserExternalId,
                    courseSummary.CourseId,
                    ParticipantStatusFilter.Active,
                    1,
                    1000,
                    studentsOnly: true,
                    includeEmail: false,
                    groupId: null,
                    cancellationToken);
                await SaveAsync(db, work, MoodleSnapshotDatasets.Students, courseSummary.CourseId, students, "hot", false, cancellationToken);
                break;
            }
            case MoodleSnapshotDatasets.Groups:
            {
                var groups = await participantsGateway.GetCourseGroupsAsync(work.UserExternalId, courseSummary.CourseId, cancellationToken);
                await SaveAsync(db, work, MoodleSnapshotDatasets.Groups, courseSummary.CourseId, groups, "warm", false, cancellationToken);
                break;
            }
            default:
                throw new InvalidOperationException($"Dataset de snapshot desconhecido: {work.Dataset}.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private static async Task<IReadOnlyList<CourseSummary>> ReadCoursesAsync(
        IMoodleCoursesGateway coursesGateway,
        string userExternalId,
        CancellationToken cancellationToken)
    {
        var courses = new List<CourseSummary>();
        for (var page = 1; page <= 10; page++)
        {
            var result = await coursesGateway.GetMyCoursesAsync(userExternalId, 100, page, cancellationToken);
            courses.AddRange(result.Items);
            if (!result.HasNextPage)
            {
                break;
            }
        }

        return courses;
    }

    private static async Task EnsureStateAsync(
        ConnectorDbContext db,
        SyncWork work,
        string dataset,
        string courseId,
        DateTimeOffset nextSyncAt,
        int priority,
        CancellationToken cancellationToken)
    {
        var state = await db.MoodleSyncStates.SingleOrDefaultAsync(
            item => item.OwnerId == work.OwnerId &&
                    item.ConnectionAlias == work.ConnectionAlias &&
                    item.Dataset == dataset &&
                    item.CourseId == courseId,
            cancellationToken);
        if (state is null)
        {
            db.MoodleSyncStates.Add(new MoodleSyncStateEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = work.OwnerId,
                ConnectionAlias = work.ConnectionAlias,
                Dataset = dataset,
                CourseId = courseId,
                ClientId = work.ClientId,
                UserExternalId = work.UserExternalId,
                Priority = priority,
                Status = "pending",
                NextSyncAt = nextSyncAt,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            return;
        }

        state.ClientId = work.ClientId;
        state.UserExternalId = work.UserExternalId;
        state.Priority = Math.Min(state.Priority == 0 ? priority : state.Priority, priority);
        if (state.Status != "running" && (state.NextSyncAt is null || nextSyncAt < state.NextSyncAt))
        {
            state.Status = "pending";
            state.NextSyncAt = nextSyncAt;
        }
        state.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task SaveAsync<T>(
        ConnectorDbContext db,
        SyncWork work,
        string type,
        string courseId,
        T payload,
        string tier,
        bool frozen,
        CancellationToken cancellationToken)
    {
        var entity = await db.MoodleSnapshots.SingleOrDefaultAsync(
            item => item.OwnerId == work.OwnerId &&
                    item.ConnectionAlias == work.ConnectionAlias &&
                    item.SnapshotType == type &&
                    item.CourseId == courseId,
            cancellationToken);
        if (entity is null)
        {
            entity = new MoodleSnapshotEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = work.OwnerId,
                ConnectionAlias = work.ConnectionAlias,
                SnapshotType = type,
                CourseId = courseId,
            };
            db.MoodleSnapshots.Add(entity);
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        var freshInterval = GetFreshInterval(type, tier, frozen);
        entity.PayloadJson = json;
        entity.Tier = tier;
        entity.IsFrozen = frozen;
        entity.UpdatedAt = now;
        entity.FreshUntil = now.Add(freshInterval);
        entity.StaleUntil = now.Add(freshInterval + GetStaleWindow(type, tier, frozen));
        entity.LastAttemptAt = now;
        entity.LastError = null;
        entity.PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        entity.IsComplete = payload switch
        {
            CourseParticipantsPage participants => !participants.HasMore,
            _ => true,
        };
        entity.RecordCount = CountRecords(payload);
    }

    private static TimeSpan GetFreshInterval(string type, string tier, bool frozen) =>
        frozen ? TimeSpan.FromDays(3650) : type switch
        {
            MoodleSnapshotDatasets.Courses => TimeSpan.FromDays(2),
            MoodleSnapshotDatasets.Activities => TimeSpan.FromHours(24),
            MoodleSnapshotDatasets.Students or MoodleSnapshotDatasets.Groups => tier.Equals("hot", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromHours(1) : TimeSpan.FromHours(4),
            _ => TimeSpan.FromHours(1),
        };

    private static TimeSpan GetStaleWindow(string type, string tier, bool frozen) =>
        frozen ? TimeSpan.Zero : type switch
        {
            MoodleSnapshotDatasets.Courses => TimeSpan.FromDays(7),
            MoodleSnapshotDatasets.Activities => tier.Equals("cold", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromDays(30) : TimeSpan.FromDays(3),
            MoodleSnapshotDatasets.Students or MoodleSnapshotDatasets.Groups => TimeSpan.FromHours(24),
            _ => TimeSpan.FromHours(12),
        };

    private static int CountRecords<T>(T payload) => payload switch
    {
        IReadOnlyCollection<CourseSummary> courses => courses.Count,
        CourseContentsSummary contents => contents.Sections.Sum(section => section.Modules.Count),
        CourseParticipantsPage participants => participants.Participants.Count,
        IReadOnlyCollection<CourseGroupSummary> groups => groups.Count,
        _ => 0,
    };

    private static DateTimeOffset GetNextAutomaticSyncAt(string dataset, DateTimeOffset now) => dataset switch
        {
            MoodleSnapshotDatasets.Connection or MoodleSnapshotDatasets.Courses => GetNextBrazilMidnight(now).AddDays(2),
            MoodleSnapshotDatasets.Activities => GetNextBrazilMidnight(now).AddDays(1),
            _ => now.Add(dataset == MoodleSnapshotDatasets.Groups ? TimeSpan.FromHours(2) : TimeSpan.FromHours(1)),
        };

    private static DateTimeOffset GetNextBrazilMidnight(DateTimeOffset now)
    {
        var timeZone = ResolveBrazilTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var nextLocalMidnight = localNow.Date.AddDays(1);
        var offset = timeZone.GetUtcOffset(nextLocalMidnight);
        return new DateTimeOffset(nextLocalMidnight, offset).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveBrazilTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
    }

    private bool TryEnqueueSignal(MoodleSnapshotSyncRequest request)
    {
        var key = BuildKey(request);
        lock (_gate)
        {
            if (!_pending.Add(key))
            {
                return false;
            }
        }

        if (_queue.Writer.TryWrite(request))
        {
            return true;
        }

        lock (_gate)
        {
            _pending.Remove(key);
        }

        // The durable row remains due and will be picked up on the next poll.
        return false;
    }

    private static MoodleSnapshotSyncRequest Normalize(MoodleSnapshotSyncRequest request)
    {
        var dataset = request.Dataset.Trim().ToLowerInvariant();
        if (dataset is not (MoodleSnapshotDatasets.Connection or MoodleSnapshotDatasets.Courses or MoodleSnapshotDatasets.Activities or MoodleSnapshotDatasets.Students or MoodleSnapshotDatasets.Groups))
        {
            dataset = MoodleSnapshotDatasets.Connection;
        }

        return request with
        {
            Dataset = dataset,
            CourseId = dataset is MoodleSnapshotDatasets.Activities or MoodleSnapshotDatasets.Students or MoodleSnapshotDatasets.Groups
                ? request.CourseId?.Trim()
                : null,
            Priority = Math.Clamp(request.Priority, 0, 1000),
        };
    }

    private static string BuildKey(MoodleSnapshotSyncRequest request) =>
        $"{request.OwnerId}:{request.ConnectionAlias}:{request.Dataset}:{request.CourseId}";

    private sealed record SyncWork(
        Guid StateId,
        Guid OwnerId,
        string ClientId,
        string ConnectionAlias,
        string UserExternalId,
        string Dataset,
        string? CourseId,
        bool Force,
        int AttemptCount);
}
