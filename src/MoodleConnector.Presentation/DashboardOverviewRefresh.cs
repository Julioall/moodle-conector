using System.Collections.Concurrent;
using System.Threading.Channels;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Submissions.Queries;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Presentation;

internal static class DashboardOverviewCache
{
    public static string Pending(Guid ownerId, string connectionAlias) =>
        $"dashboard-metric:{ownerId}:{connectionAlias}:pending";
}

internal sealed record DashboardOverviewRefreshRequest(
    Guid OwnerId,
    string ClientId,
    string ConnectionAlias,
    IReadOnlyList<CourseSummary> Courses,
    bool Force = false,
    string? ConnectionId = null);

internal interface IDashboardOverviewRefreshQueue
{
    bool Enqueue(DashboardOverviewRefreshRequest request);
    Task<bool> EnqueueAsync(DashboardOverviewRefreshRequest request, CancellationToken cancellationToken = default);
    bool IsQueued(Guid ownerId, string connectionAlias);
    Task<bool> IsQueuedAsync(Guid ownerId, string connectionAlias, CancellationToken cancellationToken = default);
}

internal sealed class DashboardOverviewRefreshQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<DashboardOverviewRefreshQueue> logger) : BackgroundService, IDashboardOverviewRefreshQueue
{
    private readonly Channel<DashboardOverviewRefreshRequest> channel =
        Channel.CreateBounded<DashboardOverviewRefreshRequest>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly ConcurrentDictionary<string, byte> queued = new(StringComparer.Ordinal);
    private readonly DateTimeOffset applicationStartedAt = DateTimeOffset.UtcNow;

    public bool Enqueue(DashboardOverviewRefreshRequest request) =>
        EnqueueAsync(request).GetAwaiter().GetResult();

    public async Task<bool> EnqueueAsync(
        DashboardOverviewRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.ConnectionAlias) ||
            request.Courses.Count == 0)
        {
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var connectionId = string.IsNullOrWhiteSpace(request.ConnectionId)
            ? await MoodleConnectionIdentity.ResolveAsync(db, request.OwnerId, request.ClientId, request.ConnectionAlias, cancellationToken)
            : request.ConnectionId.Trim();
        request = request with { ConnectionId = connectionId };
        var now = DateTimeOffset.UtcNow;
        var state = await db.MoodleSyncStates.SingleOrDefaultAsync(item =>
            item.OwnerId == request.OwnerId &&
            (item.ConnectionId == request.ConnectionId ||
             (item.ConnectionId == string.Empty && item.ConnectionAlias == request.ConnectionAlias)) &&
            item.Dataset == MoodleSnapshotDatasets.DashboardPending &&
            item.CourseId == string.Empty, cancellationToken);
        if (state is not null && MoodleSyncLeasePolicy.IsActive(state, now))
        {
            return false;
        }

        if (state is not null &&
            state.Status == "pending" &&
            state.NextSyncAt is { } nextSyncAt)
        {
            if (nextSyncAt > now)
            {
                if (!request.Force)
                {
                    return false;
                }

                // A per-course snapshot can become ready after a dashboard
                // attempt has scheduled its retry. Let an explicit force from
                // the screen retry now instead of leaving the user in a
                // preparatory state for the full backoff window.
                state.NextSyncAt = now;
                state.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }

            var dueKey = GetKey(request.OwnerId, request.ConnectionId!);
            if (!queued.TryAdd(dueKey, 0)) return false;
            if (channel.Writer.TryWrite(request)) return true;
            queued.TryRemove(dueKey, out _);
            return false;
        }

        if (state is null)
        {
            state = new MoodleSyncStateEntity
            {
                Id = Guid.NewGuid(),
                OwnerId = request.OwnerId,
                ConnectionId = request.ConnectionId!,
                ConnectionAlias = request.ConnectionAlias,
                Dataset = MoodleSnapshotDatasets.DashboardPending,
                CourseId = string.Empty,
            };
            db.MoodleSyncStates.Add(state);
        }

        state.ConnectionId = request.ConnectionId!;
        state.ConnectionAlias = request.ConnectionAlias;
        state.ClientId = request.ClientId;
        state.UserExternalId = request.OwnerId.ToString();
        state.Status = "pending";
        state.NextSyncAt = now;
        state.Priority = 5;
        state.LastError = null;
        state.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var key = GetKey(request.OwnerId, request.ConnectionId!);
        if (!queued.TryAdd(key, 0)) return false;
        if (channel.Writer.TryWrite(request)) return true;
        queued.TryRemove(key, out _);
        return false;
    }

    public bool IsQueued(Guid ownerId, string connectionAlias) =>
        queued.ContainsKey(GetKey(ownerId, connectionAlias));

    public async Task<bool> IsQueuedAsync(Guid ownerId, string connectionAlias, CancellationToken cancellationToken = default)
    {
        if (IsQueued(ownerId, connectionAlias)) return true;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        return await db.MoodleSyncStates.AsNoTracking().AnyAsync(item =>
            item.OwnerId == ownerId && item.ConnectionAlias == connectionAlias &&
            item.Dataset == MoodleSnapshotDatasets.DashboardPending && item.CourseId == string.Empty &&
            (item.Status == "pending" || item.Status == "running"), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DashboardOverviewRefreshQueue iniciada.");
        try
        {
            await RecoverOrphanedStatesAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                while (channel.Reader.TryRead(out var request))
                {
                    await ProcessRequestAsync(request, stoppingToken);
                }

                await EnqueueDueStatesAsync(stoppingToken);
                var signal = channel.Reader.WaitToReadAsync(stoppingToken).AsTask();
                var tick = Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                await Task.WhenAny(signal, tick);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("DashboardOverviewRefreshQueue encerrada.");
    }

    private async Task EnqueueDueStatesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IMoodleSnapshotStore>();
        var courseScopeResolver = scope.ServiceProvider.GetRequiredService<DashboardCourseScopeResolver>();
        var now = DateTimeOffset.UtcNow;
        var states = await db.MoodleSyncStates.AsNoTracking()
            .Where(item => item.Dataset == MoodleSnapshotDatasets.DashboardPending &&
                           item.CourseId == string.Empty && item.NextSyncAt <= now &&
                           (item.Status != "running" || item.LeaseUntil == null || item.LeaseUntil <= now))
            .OrderBy(item => item.Priority)
            .Take(16)
            .ToArrayAsync(cancellationToken);
        foreach (var state in states)
        {
            var courses = await store.GetCoursesAsync(state.OwnerId, state.ConnectionAlias, cancellationToken);
            if (courses is null) continue;
            var scopedCourses = await courseScopeResolver.FilterAsync(
                state.OwnerId,
                state.ConnectionAlias,
                courses.Data,
                cancellationToken);
            if (scopedCourses.Count == 0)
            {
                await CompleteEmptyStateAsync(db, state.Id, now, cancellationToken);
                continue;
            }
            var request = new DashboardOverviewRefreshRequest(state.OwnerId, state.ClientId, state.ConnectionAlias, scopedCourses, ConnectionId: state.ConnectionId);
            var key = GetKey(request.OwnerId, request.ConnectionId ?? request.ConnectionAlias);
            if (queued.TryAdd(key, 0) && !channel.Writer.TryWrite(request)) queued.TryRemove(key, out _);
        }
    }

    private async Task RecoverOrphanedStatesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var now = DateTimeOffset.UtcNow;
        var orphanedStates = await db.MoodleSyncStates
            .Where(item => item.Dataset == MoodleSnapshotDatasets.DashboardPending &&
                           item.CourseId == string.Empty &&
                           item.Status == "running" &&
                           (item.LastStartedAt == null || item.LastStartedAt < applicationStartedAt))
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
                "Atualizações do dashboard recuperadas após reinicialização. Count={Count}",
                recovered);
        }
    }

    private static Task<int> CompleteEmptyStateAsync(
        ConnectorDbContext db,
        Guid stateId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.MoodleSyncStates
            .Where(item => item.Id == stateId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "completed")
                .SetProperty(item => item.LastCompletedAt, now)
                .SetProperty(item => item.NextSyncAt, GetNextBrazilMidnight(now))
                .SetProperty(item => item.LastError, (string?)null)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.RecordsSynced, 0)
                .SetProperty(item => item.AttemptCount, 0)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

    private async Task ProcessRequestAsync(DashboardOverviewRefreshRequest request, CancellationToken cancellationToken)
    {
        var key = GetKey(request.OwnerId, request.ConnectionId ?? request.ConnectionAlias);
        CancellationTokenSource? leaseHeartbeatCancellation = null;
        Task? leaseHeartbeat = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var state = await TryClaimAsync(scope.ServiceProvider, request, cancellationToken);
            if (state is null) return;

            leaseHeartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            leaseHeartbeat = RenewLeaseAsync(state.Id, leaseHeartbeatCancellation.Token);

            var executionContext = scope.ServiceProvider.GetRequiredService<IConnectorExecutionContext>();
            var connectionSelection = scope.ServiceProvider.GetRequiredService<IMoodleConnectionSelection>();
            var builder = scope.ServiceProvider.GetRequiredService<DashboardPendingSnapshotBuilder>();
            executionContext.Enter(request.ClientId, request.OwnerId.ToString(), null);
            connectionSelection.Alias = request.ConnectionAlias;

            var snapshot = await builder.BuildAsync(request.OwnerId, request.Courses, cancellationToken);
            var store = scope.ServiceProvider.GetRequiredService<IMoodleSnapshotStore>();
            var generatedAt = snapshot.SnapshotGeneratedAt ?? DateTimeOffset.UtcNow;
            var snapshotComplete = snapshot.CoursesAnalyzed >= snapshot.CoursesInScope;
            var snapshotError = snapshotComplete
                ? null
                : $"A leitura de pendências ficou incompleta em {snapshot.CoursesInScope - snapshot.CoursesAnalyzed} curso(s).";
            await store.SaveAsync(
                request.OwnerId,
                request.ConnectionAlias,
                MoodleSnapshotDatasets.DashboardPending,
                string.Empty,
                snapshot,
                "hot",
                frozen: false,
                complete: snapshotComplete,
                snapshot.CoursesAnalyzed,
                generatedAt,
                cancellationToken);

            var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
            var persistedState = await db.MoodleSyncStates.SingleAsync(item => item.Id == state.Id, cancellationToken);
            var completedAt = DateTimeOffset.UtcNow;
            persistedState.Status = snapshotComplete ? "completed" : "pending";
            if (snapshotComplete)
            {
                persistedState.LastCompletedAt = completedAt;
            }
            persistedState.NextSyncAt = snapshotComplete
                ? GetNextBrazilMidnight(completedAt)
                : completedAt.AddSeconds(30);
            persistedState.LastError = snapshotError;
            persistedState.LeaseUntil = null;
            persistedState.RecordsSynced = snapshot.CoursesAnalyzed;
            persistedState.AttemptCount = 0;
            persistedState.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(request, exception, cancellationToken);
            logger.LogError(
                exception,
                "Falha ao atualizar a visão geral do dashboard. OwnerId={OwnerId} Connection={ConnectionAlias}",
                request.OwnerId,
                request.ConnectionAlias);
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
            queued.TryRemove(key, out _);
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
            logger.LogWarning(exception, "Não foi possível renovar o lease da atualização do dashboard. StateId={StateId}", stateId);
        }
    }

    private static async Task<MoodleSyncStateEntity?> TryClaimAsync(
        IServiceProvider services,
        DashboardOverviewRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ConnectorDbContext>();
        var connectionId = string.IsNullOrWhiteSpace(request.ConnectionId)
            ? await MoodleConnectionIdentity.ResolveAsync(db, request.OwnerId, request.ClientId, request.ConnectionAlias, cancellationToken)
            : request.ConnectionId.Trim();
        var state = await db.MoodleSyncStates.AsNoTracking().SingleOrDefaultAsync(item =>
            item.OwnerId == request.OwnerId &&
            (item.ConnectionId == connectionId ||
             (item.ConnectionId == string.Empty && item.ConnectionAlias == request.ConnectionAlias)) &&
            item.Dataset == MoodleSnapshotDatasets.DashboardPending && item.CourseId == string.Empty,
            cancellationToken);
        if (state is null || state.NextSyncAt is not { } next || next > DateTimeOffset.UtcNow) return null;
        var now = DateTimeOffset.UtcNow;
        if (MoodleSyncLeasePolicy.IsActive(state, now)) return null;
        var affected = await db.MoodleSyncStates.Where(item =>
                item.Id == state.Id &&
                (item.Status != "running" || item.LeaseUntil == null || item.LeaseUntil <= now) &&
                (item.LeaseUntil == null || item.LeaseUntil <= now) && item.NextSyncAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "running")
                .SetProperty(item => item.LastStartedAt, now)
                .SetProperty(item => item.LastAttemptAt, now)
                .SetProperty(item => item.LeaseUntil, now.AddMinutes(30))
                .SetProperty(item => item.AttemptCount, state.AttemptCount + 1)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);
        return affected == 1 ? state : null;
    }

    private async Task MarkFailedAsync(DashboardOverviewRefreshRequest request, Exception exception, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        var connectionId = string.IsNullOrWhiteSpace(request.ConnectionId)
            ? await MoodleConnectionIdentity.ResolveAsync(db, request.OwnerId, request.ClientId, request.ConnectionAlias, cancellationToken)
            : request.ConnectionId.Trim();
        var state = await db.MoodleSyncStates.SingleOrDefaultAsync(item =>
            item.OwnerId == request.OwnerId &&
            (item.ConnectionId == connectionId ||
             (item.ConnectionId == string.Empty && item.ConnectionAlias == request.ConnectionAlias)) &&
            item.Dataset == MoodleSnapshotDatasets.DashboardPending && item.CourseId == string.Empty,
            cancellationToken);
        if (state is null) return;
        var seconds = Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, state.AttemptCount - 1))) * (0.75 + Random.Shared.NextDouble() * 0.5);
        state.Status = "failed";
        var safeError = MoodleConnector.Application.MoodleApi.MoodleErrorContract.Describe(exception).Message;
        state.LastError = safeError.Length > 4000 ? safeError[..4000] : safeError;
        state.NextSyncAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        state.LeaseUntil = null;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string GetKey(Guid ownerId, string connectionRef) =>
        $"{ownerId}:{connectionRef}";

    private static DateTimeOffset GetNextBrazilMidnight(DateTimeOffset now)
    {
        var timeZone = ResolveBrazilTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var nextLocalMidnight = localNow.Date.AddDays(1);
        return new DateTimeOffset(nextLocalMidnight, timeZone.GetUtcOffset(nextLocalMidnight)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveBrazilTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
    }
}

internal sealed class DashboardPendingSnapshotBuilder(
    IServiceScopeFactory scopeFactory,
    ConnectorDbContext dbContext,
    IConnectorExecutionContext executionContext,
    IMoodleConnectionSelection connectionSelection)
{
    public async Task<AppDashboardPendingMetricDto> BuildAsync(
        Guid ownerId,
        IReadOnlyList<CourseSummary> courses,
        CancellationToken cancellationToken)
    {
        var clientId = executionContext.ClientId
            ?? throw new InvalidOperationException("O contexto de execução do dashboard não possui ClientId.");
        var subject = executionContext.Subject ?? ownerId.ToString();
        var pendingResults = await ReadPendingAsync(
            ownerId,
            courses,
            scopeFactory,
            clientId,
            subject,
            executionContext.Email,
            executionContext.Scopes,
            connectionSelection.Alias,
            ownerId.ToString(),
            cancellationToken);
        var pendingRows = pendingResults.SelectMany(item => item.Rows).ToArray();
        var gradingRows = pendingResults.SelectMany(item => item.GradingRows).ToArray();
        var pendingItems = pendingResults.SelectMany(item => item.PendingItems).ToArray();
        var pendingStudentIds = pendingResults
            .SelectMany(item => item.Rows.Select(row => row.StudentId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overdueStudentIds = pendingResults
            .SelectMany(item => item.Rows.Where(row => row.Level == "risk").Select(row => row.StudentId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warnings = pendingResults
            .SelectMany(item => item.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var coursesAnalyzed = pendingResults.Count(item => item.IsComplete);
        if (coursesAnalyzed < courses.Count)
        {
            warnings.Insert(0,
                $"A leitura de pendências ficou incompleta em {courses.Count - coursesAnalyzed} de {courses.Count} curso(s). " +
                "Os dados exibidos não representam todo o escopo.");
        }
        var courseSummaries = pendingResults
            .Select(item => new AppDashboardCoursePendingSummaryDto(
                item.CourseId,
                item.CourseName,
                item.CorrectionActivities,
                item.GradingRows.Count,
                item.PendingActivities,
                item.Rows.Count,
                item.GradingRows.Select(row => row.StudentId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                item.Rows.Select(row => row.StudentId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                item.Rows.Count(row => row.Level == "risk"),
                item.Warnings.Any(warning => warning.Contains("limitad", StringComparison.OrdinalIgnoreCase)),
                item.Warnings.FirstOrDefault()))
            .Where(item => item.PendingCorrectionActivities > 0 || item.PendingSubmissionActivities > 0)
            .OrderByDescending(item => item.PendingCorrectionSubmissions + item.PendingSubmissions)
            .ThenBy(item => item.CourseName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var summary = new AppDashboardSummaryDto(
            courses.Count,
            pendingRows.Length,
            courseSummaries.Sum(item => item.PendingCorrectionSubmissions),
            overdueStudentIds.Count,
            pendingStudentIds.Count)
        {
            ActivitiesToReview = courseSummaries.Sum(item => item.PendingCorrectionSubmissions),
            PendingSubmissionAssignments = pendingRows.Length,
            PendingCorrectionAssignments = courseSummaries.Sum(item => item.PendingCorrectionSubmissions),
        };

        return new AppDashboardPendingMetricDto(
            summary,
            pendingResults.SelectMany(item => item.PriorityRows).Concat(gradingRows).Take(AppDashboardBudget.MaxPriorities).ToArray(),
            gradingRows.Take(AppDashboardBudget.MaxCorrectionItems).ToArray(),
            courseSummaries,
            await ReadTodayItemsAsync(ownerId, cancellationToken),
            warnings.ToArray())
        {
            IsRefreshing = false,
            CoursesInScope = courses.Count,
            CoursesAnalyzed = coursesAnalyzed,
            SnapshotGeneratedAt = DateTimeOffset.UtcNow,
            PendingItems = pendingItems,
        };
    }

    public async Task<AppDashboardPendingMetricDto> CreateRefreshingAsync(
        Guid ownerId,
        int coursesInScope,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return new AppDashboardPendingMetricDto(
            new AppDashboardSummaryDto(coursesInScope, 0, 0, 0, 0),
            [],
            [],
            [],
            await ReadTodayItemsAsync(ownerId, cancellationToken),
            ["A visão geral das atividades pendentes está sendo atualizada no Moodle."])
        {
            IsRefreshing = true,
            CoursesInScope = coursesInScope,
            CoursesAnalyzed = 0,
            SnapshotGeneratedAt = null,
        };
    }

    public async Task<AppDashboardPendingMetricDto> CreateEmptyAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return new AppDashboardPendingMetricDto(
            new AppDashboardSummaryDto(0, 0, 0, 0, 0),
            [],
            [],
            [],
            await ReadTodayItemsAsync(ownerId, cancellationToken),
            [])
        {
            IsRefreshing = false,
            CoursesInScope = 0,
            CoursesAnalyzed = 0,
            SnapshotGeneratedAt = null,
        };
    }

    private static async Task<IReadOnlyList<DashboardPendingRead>> ReadPendingAsync(
        Guid ownerId,
        IReadOnlyList<CourseSummary> courses,
        IServiceScopeFactory scopeFactory,
        string clientId,
        string subject,
        string? email,
        IReadOnlyCollection<string> scopes,
        string? connectionAlias,
        string userExternalId,
        CancellationToken cancellationToken)
    {
        // This step only reads local snapshots. Missing data is queued for the
        // per-course worker so opening the dashboard never fans out live Moodle
        // calls for every tracked course.
        using var limiter = new SemaphoreSlim(AppDashboardBudget.PendingCourseConcurrency);
        var tasks = courses.Select(async course =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                await using var courseScope = scopeFactory.CreateAsyncScope();
                var courseExecutionContext = courseScope.ServiceProvider.GetRequiredService<IConnectorExecutionContext>();
                courseExecutionContext.Enter(clientId, subject, email, scopes);
                var courseConnectionSelection = courseScope.ServiceProvider.GetRequiredService<IMoodleConnectionSelection>();
                courseConnectionSelection.Alias = connectionAlias;
                var courseMediator = courseScope.ServiceProvider.GetRequiredService<IMediator>();
                CourseContentsSummary? prefetchedContents = null;
                CourseParticipantsPage? prefetchedParticipants = null;
                CourseAssignmentSubmissionsSnapshot? prefetchedSubmissions = null;
                if (!string.IsNullOrWhiteSpace(connectionAlias))
                {
                    var snapshotStore = courseScope.ServiceProvider.GetRequiredService<IMoodleSnapshotStore>();
                    var activitySnapshot = await snapshotStore.GetActivitiesAsync(
                        ownerId,
                        connectionAlias!,
                        course.CourseId,
                        cancellationToken);
                    if (activitySnapshot?.IsComplete == true)
                    {
                        prefetchedContents = activitySnapshot.Data;
                    }

                    var participantSnapshot = await snapshotStore.GetStudentsAsync(
                        ownerId,
                        connectionAlias!,
                        course.CourseId,
                        cancellationToken);
                    if (participantSnapshot?.IsComplete == true)
                    {
                        prefetchedParticipants = participantSnapshot.Data;
                    }

                    var submissionsSnapshot = await snapshotStore.GetAsync<CourseAssignmentSubmissionsSnapshot>(
                        ownerId,
                        connectionAlias!,
                        MoodleSnapshotDatasets.Submissions,
                        course.CourseId,
                        cancellationToken);
                    if (submissionsSnapshot?.IsComplete == true)
                    {
                        prefetchedSubmissions = submissionsSnapshot.Data;
                    }

                    var snapshotsReady = prefetchedContents is not null &&
                                         prefetchedParticipants is not null &&
                                         prefetchedSubmissions is not null;
                    if (!snapshotsReady)
                    {
                        var snapshotQueue = courseScope.ServiceProvider.GetRequiredService<IMoodleSnapshotSyncQueue>();
                        await snapshotQueue.EnqueueAsync(new MoodleSnapshotSyncRequest(
                            ownerId,
                            clientId,
                            connectionAlias!,
                            userExternalId,
                            Dataset: MoodleSnapshotDatasets.Submissions,
                            CourseId: course.CourseId,
                            Priority: 10,
                            // An incomplete submission snapshot already has a
                            // scheduled retry. Force only the legacy case in
                            // which a complete submission snapshot lacks its
                            // companion activities or students snapshot.
                            Force: submissionsSnapshot?.IsComplete == true), cancellationToken);
                        return new DashboardPendingRead(
                            course.CourseId,
                            course.FullName,
                            [],
                            [],
                            [],
                            [],
                            0,
                            0,
                            [$"[{course.CourseId}] {course.FullName}: os dados locais deste curso estão sendo preparados."],
                            false);
                    }

                    if (activitySnapshot!.IsStale || participantSnapshot!.IsStale || submissionsSnapshot!.IsStale)
                    {
                        var snapshotQueue = courseScope.ServiceProvider.GetRequiredService<IMoodleSnapshotSyncQueue>();
                        await snapshotQueue.EnqueueAsync(new MoodleSnapshotSyncRequest(
                            ownerId,
                            clientId,
                            connectionAlias!,
                            userExternalId,
                            Dataset: MoodleSnapshotDatasets.Submissions,
                            CourseId: course.CourseId,
                            Priority: 10,
                            Force: true), cancellationToken);
                    }
                }
                else
                {
                    return new DashboardPendingRead(
                        course.CourseId,
                        course.FullName,
                        [],
                        [],
                        [],
                        [],
                        0,
                        0,
                        [$"[{course.CourseId}] {course.FullName}: a conexão Moodle não está disponível para preparar o snapshot."],
                        false);
                }
                var pending = await courseMediator.Send(new GetStudentsWithPendingSubmissionsQuery(
                    course.CourseId,
                    DueDaysAhead: 0,
                    MaxStudentsToAnalyze: AppDashboardBudget.MaxParticipantsRead,
                    IncludeAwaitingGrading: true,
                    MaxAssignmentsToAnalyze: AppDashboardBudget.MaxAssignmentsRead,
                    PrefetchedContents: prefetchedContents,
                    PrefetchedParticipants: prefetchedParticipants,
                    PrefetchedSubmissions: prefetchedSubmissions), cancellationToken);

                var rows = pending.Students
                    .SelectMany(student => student.PendingAssignments.Select(activity => new DashboardPendingRow(
                        student.StudentId,
                        activity.IsOverdue ? "risk" : "attention")))
                    .ToArray();
                var pendingItems = pending.Students
                    .SelectMany(student => student.PendingAssignments.Select(activity => new AppDashboardPendingItemDto(
                        course.CourseId,
                        student.StudentId,
                        student.FullName,
                        student.LastCourseAccessAt,
                        activity.AssignmentId,
                        activity.AssignmentName,
                        activity.DueDate,
                        activity.IsOverdue)))
                    .ToArray();
                var priorityRows = pending.Students
                    .SelectMany(student => student.PendingAssignments.Select(activity => new AppDashboardPriorityDto(
                        $"{course.CourseId}:{student.StudentId}:{activity.AssignmentId}",
                        "Entrega pendente",
                        $"{student.FullName} · {activity.AssignmentName}",
                        activity.IsOverdue ? "risk" : "attention",
                        course.CourseId,
                        student.StudentId)))
                    .ToArray();
                var gradingRows = pending.AwaitingGrading
                    .Select(item => new AppDashboardPriorityDto(
                        $"{course.CourseId}:{item.StudentId}:{item.Item.AssignmentId}:grading",
                        "Atividade para corrigir",
                        $"{item.FullName} · {item.Item.AssignmentName}",
                        "attention",
                        course.CourseId,
                        item.StudentId))
                    .ToArray();
                var pendingActivities = pending.Students
                    .SelectMany(student => student.PendingAssignments)
                    .Select(activity => activity.AssignmentId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var correctionActivities = pending.AwaitingGrading
                    .Select(item => item.Item.AssignmentId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var warnings = pending.Warning is null
                    ? Array.Empty<string>()
                    : [$"[{course.CourseId}] {course.FullName}: {pending.Warning}"];
                return new DashboardPendingRead(
                    course.CourseId,
                    course.FullName,
                    rows,
                    pendingItems,
                    priorityRows,
                    gradingRows,
                    pendingActivities,
                    correctionActivities,
                    warnings,
                    pending.IsComplete);
            }
            catch
            {
                return new DashboardPendingRead(
                    course.CourseId,
                    course.FullName,
                    [],
                    [],
                    [],
                    [],
                    0,
                    0,
                    [$"Não foi possível carregar as pendências do curso {course.FullName} (courseId={course.CourseId})."],
                    false);
            }
            finally
            {
                limiter.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<AppDashboardTodayItemDto>> ReadTodayItemsAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var todayStart = GetBrazilTodayStart(DateTimeOffset.UtcNow);
        var todayEnd = todayStart.AddDays(1);
        var tasks = await dbContext.Tasks.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Status != "done" && item.DueAt >= todayStart && item.DueAt < todayEnd)
            .OrderBy(item => item.DueAt)
            .Take(AppDashboardBudget.MaxPriorities)
            .Select(item => new AppDashboardTodayItemDto($"task:{item.Id}", "task", item.Title, "Tarefa", item.DueAt))
            .ToArrayAsync(cancellationToken);
        var events = await dbContext.CalendarEvents.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.StartAt >= todayStart && item.StartAt < todayEnd)
            .OrderBy(item => item.StartAt)
            .Take(AppDashboardBudget.MaxPriorities)
            .Select(item => new AppDashboardTodayItemDto($"event:{item.Id}", "event", item.Title, "Evento", item.StartAt))
            .ToArrayAsync(cancellationToken);

        return tasks.Concat(events)
            .OrderBy(item => item.StartsAt)
            .Take(AppDashboardBudget.MaxPriorities)
            .ToArray();
    }

    private static DateTimeOffset GetBrazilTodayStart(DateTimeOffset value)
    {
        var brazil = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");
        var local = TimeZoneInfo.ConvertTime(value, brazil);
        var date = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(date, brazil.GetUtcOffset(date)).ToUniversalTime();
    }

    private sealed record DashboardPendingRead(
        string CourseId,
        string CourseName,
        IReadOnlyList<DashboardPendingRow> Rows,
        IReadOnlyList<AppDashboardPendingItemDto> PendingItems,
        IReadOnlyList<AppDashboardPriorityDto> PriorityRows,
        IReadOnlyList<AppDashboardPriorityDto> GradingRows,
        int PendingActivities,
        int CorrectionActivities,
        IReadOnlyList<string> Warnings,
        bool IsComplete);

    private sealed record DashboardPendingRow(string StudentId, string Level);
}
