using Microsoft.EntityFrameworkCore;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Presentation.Endpoints;

internal static class AdminMetricsEndpoints
{
    public static void Map(WebApplication app, string rateLimitPolicy)
    {
        app.MapGet("/api/admin/metrics", async (
            int? hours,
            HttpContext context,
            ConnectorDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (await PortalEndpointAuthorization.ResolveAppIdentityAsync(context, dbContext, cancellationToken) is null) return Results.Unauthorized();
            if (!PortalEndpointAuthorization.HasAppPermission(context, AppPermissionCatalog.AdminView)) return Results.Forbid();

            var periodHours = Math.Clamp(hours ?? 168, 1, 24 * 30);
            var generatedAt = DateTimeOffset.UtcNow;
            var cutoff = generatedAt.AddHours(-periodHours);
            var requests = dbContext.PlatformRequestMetrics.AsNoTracking().Where(item => item.RecordedAtUtc >= cutoff);
            var totalRequests = await requests.CountAsync(cancellationToken);
            var failedRequests = await requests.CountAsync(item => item.StatusCode >= 400, cancellationToken);
            var averageDurationMs = totalRequests == 0
                ? 0
                : await requests.AverageAsync(item => (double)item.DurationMs, cancellationToken);
            var activeEndpoints = await requests.Select(item => item.Endpoint).Distinct().CountAsync(cancellationToken);

            var endpointMetrics = await requests
                .GroupBy(item => new { item.Endpoint, item.Method })
                .Select(group => new AdminEndpointMetricDto(
                    group.Key.Endpoint,
                    group.Key.Method,
                    group.Count(),
                    group.Count(item => item.StatusCode >= 400),
                    group.Average(item => (double)item.DurationMs)))
                .OrderByDescending(item => item.Requests)
                .ThenBy(item => item.Endpoint)
                .Take(12)
                .ToArrayAsync(cancellationToken);

            var auditLogs = dbContext.MoodleAuditLogs.AsNoTracking().Where(item => item.CreatedAt >= cutoff);
            var toolMetrics = await auditLogs
                .GroupBy(item => item.ToolName)
                .Select(group => new AdminToolMetricDto(
                    group.Key,
                    group.Count(),
                    group.Count(item => item.ErrorCode != null || item.Status == "failed" || item.Status == "error"),
                    group.Average(item => item.DurationMs == null ? 0d : (double)item.DurationMs)))
                .OrderByDescending(item => item.Errors)
                .ThenByDescending(item => item.Invocations)
                .Take(12)
                .ToArrayAsync(cancellationToken);

            var requestErrors = await requests.Where(item => item.StatusCode >= 400)
                .OrderByDescending(item => item.RecordedAtUtc).Take(20)
                .Select(item => new AdminOperationalErrorDto(item.RecordedAtUtc, "plataforma", item.Endpoint, item.FailureKind ?? $"http_{item.StatusCode}", item.StatusCode, item.DurationMs))
                .ToArrayAsync(cancellationToken);
            var toolErrors = await auditLogs.Where(item => item.ErrorCode != null || item.Status == "failed" || item.Status == "error")
                .OrderByDescending(item => item.CreatedAt).Take(20)
                .Select(item => new AdminOperationalErrorDto(item.CreatedAt, "tool", item.ToolName, item.ErrorCode ?? item.Status, null, item.DurationMs ?? 0))
                .ToArrayAsync(cancellationToken);
            var errors = requestErrors.Concat(toolErrors).OrderByDescending(item => item.OccurredAt).Take(20).ToArray();

            return Results.Ok(new AdminMetricsDto(
                generatedAt,
                periodHours,
                totalRequests,
                failedRequests,
                Math.Round(averageDurationMs, 1),
                activeEndpoints,
                endpointMetrics,
                toolMetrics,
                errors));
        }).RequireRateLimiting(rateLimitPolicy);
    }
}

public sealed record AdminMetricsDto(DateTimeOffset GeneratedAt, int PeriodHours, int TotalRequests, int FailedRequests, double AverageDurationMs, int ActiveEndpoints, IReadOnlyList<AdminEndpointMetricDto> Endpoints, IReadOnlyList<AdminToolMetricDto> Tools, IReadOnlyList<AdminOperationalErrorDto> Errors);
public sealed record AdminEndpointMetricDto(string Endpoint, string Method, int Requests, int Errors, double AverageDurationMs);
public sealed record AdminToolMetricDto(string ToolName, int Invocations, int Errors, double AverageDurationMs);
public sealed record AdminOperationalErrorDto(DateTimeOffset OccurredAt, string Source, string Operation, string Code, int? StatusCode, long DurationMs);
