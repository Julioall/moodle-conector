using System.Diagnostics;
using System.Diagnostics.Metrics;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

/// <summary>
/// Records aggregate Moodle Web Service costs without retaining request
/// parameters, credentials, student data, or other sensitive payloads.
/// </summary>
public sealed class MoodleCallMetrics : IMoodleCallTelemetry, IDisposable
{
    private readonly Meter meter = new("MoodleConnector.MoodleCalls", "1.0.0");
    private readonly Counter<long> calls;
    private readonly Counter<long> failures;
    private readonly Histogram<double> durationMs;

    public MoodleCallMetrics()
    {
        calls = meter.CreateCounter<long>("moodle_webservice_calls");
        failures = meter.CreateCounter<long>("moodle_webservice_failures");
        durationMs = meter.CreateHistogram<double>("moodle_webservice_duration_ms");
    }

    public void RecordMoodleWebServiceCall(string? connectionAlias = null) =>
        RecordMoodleWebServiceCall(connectionAlias, "unknown");

    public void RecordMoodleWebServiceCall(string? connectionAlias, string? functionName) =>
        calls.Add(1, Tags(connectionAlias, functionName));

    public void RecordMoodleWebServiceCompleted(string? connectionAlias, string? functionName, double duration) =>
        durationMs.Record(Math.Max(0, duration), Tags(connectionAlias, functionName));

    public void RecordMoodleWebServiceFailure(string? connectionAlias, string? functionName, string? errorCode, double duration)
    {
        var tags = Tags(connectionAlias, functionName);
        tags.Add("error_code", string.IsNullOrWhiteSpace(errorCode) ? "unknown" : errorCode);
        failures.Add(1, tags);
        durationMs.Record(Math.Max(0, duration), tags);
    }

    public void Dispose() => meter.Dispose();

    private static TagList Tags(string? connectionAlias, string? functionName) => new()
    {
        { "connection", string.IsNullOrWhiteSpace(connectionAlias) ? "unknown" : connectionAlias },
        { "function", string.IsNullOrWhiteSpace(functionName) ? "unknown" : functionName },
    };
}
