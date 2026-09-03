using System.Diagnostics;
using System.Diagnostics.Metrics;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

public sealed class GradingOperationTelemetry : IGradingOperationTelemetry, IDisposable
{
    private readonly Meter meter = new("MoodleConnector.Grading", "1.0.0");
    private readonly Histogram<double> duration;
    private readonly Counter<long> operations;
    private readonly Histogram<long> queries;
    private readonly Histogram<long> items;
    private readonly Histogram<long> bytesProcessed;
    private readonly Counter<long> resourceRegisterCount;
    private readonly Counter<long> resourceReadCount;
    private readonly Histogram<double> resourceReadDuration;
    private readonly Histogram<double> resourceDownloadDuration;
    private readonly Histogram<long> resourceDownloadBytes;
    private readonly Counter<long> resourceCacheHit;
    private readonly Counter<long> resourceCacheMiss;
    private readonly Counter<long> resourceReadFailure;
    private readonly Counter<long> legacyFallbackCount;

    public GradingOperationTelemetry()
    {
        duration = meter.CreateHistogram<double>("grading_phase_duration_ms");
        operations = meter.CreateCounter<long>("grading_phase_operations");
        queries = meter.CreateHistogram<long>("grading_phase_sql_queries");
        items = meter.CreateHistogram<long>("grading_phase_items");
        bytesProcessed = meter.CreateHistogram<long>("grading_phase_bytes");
        resourceRegisterCount = meter.CreateCounter<long>("resource_register_count");
        resourceReadCount = meter.CreateCounter<long>("resource_read_count");
        resourceReadDuration = meter.CreateHistogram<double>("resource_read_duration_ms");
        resourceDownloadDuration = meter.CreateHistogram<double>("resource_download_duration_ms");
        resourceDownloadBytes = meter.CreateHistogram<long>("resource_download_bytes");
        resourceCacheHit = meter.CreateCounter<long>("resource_cache_hit");
        resourceCacheMiss = meter.CreateCounter<long>("resource_cache_miss");
        resourceReadFailure = meter.CreateCounter<long>("resource_read_failure");
        legacyFallbackCount = meter.CreateCounter<long>("legacy_fallback_count");
    }

    public void RecordPhase(string operation, string phase, string result, double durationMs, int queryCount = 0, int itemCount = 0, long bytes = 0)
    {
        var tags = new TagList
        {
            { "operation", Normalize(operation) },
            { "phase", Normalize(phase) },
            { "result", Normalize(result) }
        };
        operations.Add(1, tags);
        duration.Record(Math.Max(0, durationMs), tags);
        queries.Record(Math.Max(0, queryCount), tags);
        items.Record(Math.Max(0, itemCount), tags);
        bytesProcessed.Record(Math.Max(0, bytes), tags);

        if (string.Equals(phase, "legacy_fallback", StringComparison.Ordinal))
        {
            legacyFallbackCount.Add(1, new TagList { { "reason", Normalize(result) } });
        }

        if (!string.Equals(operation, "moodle_resource", StringComparison.Ordinal))
        {
            return;
        }

        var resourceTags = new TagList { { "result", Normalize(result) } };
        if (string.Equals(phase, "register", StringComparison.Ordinal))
        {
            resourceRegisterCount.Add(1, resourceTags);
            return;
        }

        if (!string.Equals(phase, "read", StringComparison.Ordinal))
        {
            return;
        }

        resourceReadCount.Add(1, resourceTags);
        resourceReadDuration.Record(Math.Max(0, durationMs), resourceTags);
        if (string.Equals(result, "cache_hit", StringComparison.Ordinal))
        {
            resourceCacheHit.Add(1, resourceTags);
            return;
        }

        if (string.Equals(result, "success", StringComparison.Ordinal))
        {
            resourceCacheMiss.Add(1, resourceTags);
            resourceDownloadDuration.Record(Math.Max(0, durationMs), resourceTags);
            resourceDownloadBytes.Record(Math.Max(0, bytes), resourceTags);
            return;
        }

        resourceReadFailure.Add(1, resourceTags);
    }

    public void Dispose() => meter.Dispose();

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim()[..Math.Min(64, value.Trim().Length)];
}
