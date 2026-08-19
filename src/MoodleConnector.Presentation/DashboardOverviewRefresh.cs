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
    IReadOnlyList<CourseSummary> Courses);

internal interface IDashboardOverviewRefreshQueue
{
    bool Enqueue(DashboardOverviewRefreshRequest request);
    bool IsQueued(Guid ownerId, string connectionAlias);
}

internal sealed class DashboardOverviewRefreshQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<DashboardOverviewRefreshQueue> logger) : BackgroundService, IDashboardOverviewRefreshQueue
{
    private readonly Channel<DashboardOverviewRefreshRequest> channel =
        Channel.CreateUnbounded<DashboardOverviewRefreshRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly ConcurrentDictionary<string, byte> queued = new(StringComparer.Ordinal);

    public bool Enqueue(DashboardOverviewRefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId) ||
            string.IsNullOrWhiteSpace(request.ConnectionAlias) ||
            request.Courses.Count == 0)
        {
            return false;
        }

        var key = GetKey(request.OwnerId, request.ConnectionAlias);
        if (!queued.TryAdd(key, 0))
        {
            return false;
        }

        if (channel.Writer.TryWrite(request))
        {
            return true;
        }

        queued.TryRemove(key, out _);
        return false;
    }

    public bool IsQueued(Guid ownerId, string connectionAlias) =>
        queued.ContainsKey(GetKey(ownerId, connectionAlias));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DashboardOverviewRefreshQueue iniciada.");
        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
            {
                var key = GetKey(request.OwnerId, request.ConnectionAlias);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var executionContext = scope.ServiceProvider.GetRequiredService<IConnectorExecutionContext>();
                    var connectionSelection = scope.ServiceProvider.GetRequiredService<IMoodleConnectionSelection>();
                    var builder = scope.ServiceProvider.GetRequiredService<DashboardPendingSnapshotBuilder>();

                    executionContext.Enter(request.ClientId, request.OwnerId.ToString(), null);
                    connectionSelection.Alias = request.ConnectionAlias;

                    var snapshot = await builder.BuildAsync(request.OwnerId, request.Courses, stoppingToken);
                    var memoryCache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                    memoryCache.Set(
                        DashboardOverviewCache.Pending(request.OwnerId, request.ConnectionAlias),
                        snapshot,
                        AppDashboardBudget.MetricCacheDuration);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Falha ao atualizar a visão geral do dashboard. OwnerId={OwnerId} Connection={ConnectionAlias}",
                        request.OwnerId,
                        request.ConnectionAlias);
                }
                finally
                {
                    queued.TryRemove(key, out _);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("DashboardOverviewRefreshQueue encerrada.");
    }

    private static string GetKey(Guid ownerId, string connectionAlias) =>
        $"{ownerId}:{connectionAlias}";
}

internal sealed class DashboardPendingSnapshotBuilder(
    IMediator mediator,
    ConnectorDbContext dbContext)
{
    public async Task<AppDashboardPendingMetricDto> BuildAsync(
        Guid ownerId,
        IReadOnlyList<CourseSummary> courses,
        CancellationToken cancellationToken)
    {
        var pendingResults = await ReadPendingAsync(courses, mediator, ownerId.ToString(), cancellationToken);
        var pendingRows = pendingResults.SelectMany(item => item.Rows).ToArray();
        var gradingRows = pendingResults.SelectMany(item => item.GradingRows).ToArray();
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
            CoursesAnalyzed = courses.Count,
            SnapshotGeneratedAt = DateTimeOffset.UtcNow,
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

    private static async Task<IReadOnlyList<DashboardPendingRead>> ReadPendingAsync(
        IReadOnlyList<CourseSummary> courses,
        IMediator mediator,
        string userExternalId,
        CancellationToken cancellationToken)
    {
        // Cada curso pode consultar vários status de feedback em paralelo.
        // Dois cursos simultâneos mantêm a leitura responsiva sem pressionar
        // o endpoint do Moodle durante a atualização do painel completo.
        using var limiter = new SemaphoreSlim(2, 2);
        var tasks = courses.Select(async course =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                var pending = await mediator.Send(new GetStudentsWithPendingSubmissionsQuery(
                    course.CourseId,
                    DueDaysAhead: 0,
                    MaxStudentsToAnalyze: AppDashboardBudget.MaxParticipantsRead,
                    IncludeAwaitingGrading: true,
                    MaxAssignmentsToAnalyze: AppDashboardBudget.MaxAssignmentsRead), cancellationToken);

                var rows = pending.Students
                    .SelectMany(student => student.PendingAssignments.Select(activity => new DashboardPendingRow(
                        student.StudentId,
                        activity.IsOverdue ? "risk" : "attention")))
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
                    : [$"{course.FullName}: {pending.Warning}"];
                return new DashboardPendingRead(
                    course.CourseId,
                    course.FullName,
                    rows,
                    priorityRows,
                    gradingRows,
                    pendingActivities,
                    correctionActivities,
                    warnings);
            }
            catch
            {
                return new DashboardPendingRead(
                    course.CourseId,
                    course.FullName,
                    [],
                    [],
                    [],
                    0,
                    0,
                    [$"Não foi possível carregar as pendências do curso {course.FullName}."]);
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
        IReadOnlyList<AppDashboardPriorityDto> PriorityRows,
        IReadOnlyList<AppDashboardPriorityDto> GradingRows,
        int PendingActivities,
        int CorrectionActivities,
        IReadOnlyList<string> Warnings);

    private sealed record DashboardPendingRow(string StudentId, string Level);
}
