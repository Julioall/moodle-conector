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

    public GradingOperationTelemetry()
    {
        duration = meter.CreateHistogram<double>("grading_phase_duration_ms");
        operations = meter.CreateCounter<long>("grading_phase_operations");
        queries = meter.CreateHistogram<long>("grading_phase_sql_queries");
        items = meter.CreateHistogram<long>("grading_phase_items");
        bytesProcessed = meter.CreateHistogram<long>("grading_phase_bytes");
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
    }

    public void Dispose() => meter.Dispose();

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim()[..Math.Min(64, value.Trim().Length)];
}
