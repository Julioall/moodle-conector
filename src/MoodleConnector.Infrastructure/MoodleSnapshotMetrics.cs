using System.Diagnostics.Metrics;

namespace MoodleConnector.Infrastructure;

public sealed class MoodleSnapshotMetrics : IDisposable
{
    private readonly Meter meter = new("MoodleConnector.Snapshots", "1.0.0");
    private readonly Counter<long> l1Hits;
    private readonly Counter<long> l2Hits;
    private readonly Counter<long> misses;
    private readonly Counter<long> refreshes;
    private readonly Histogram<double> snapshotAgeSeconds;
    private readonly Histogram<double> syncDurationMs;

    public MoodleSnapshotMetrics()
    {
        l1Hits = meter.CreateCounter<long>("moodle_snapshot_l1_hits");
        l2Hits = meter.CreateCounter<long>("moodle_snapshot_l2_hits");
        misses = meter.CreateCounter<long>("moodle_snapshot_misses");
        refreshes = meter.CreateCounter<long>("moodle_snapshot_refreshes");
        snapshotAgeSeconds = meter.CreateHistogram<double>("moodle_snapshot_age_seconds");
        syncDurationMs = meter.CreateHistogram<double>("moodle_snapshot_sync_duration_ms");
    }

    public void RecordL1Hit(string dataset) => l1Hits.Add(1, new KeyValuePair<string, object?>("dataset", dataset));

    public void RecordL2Hit(string dataset, DateTimeOffset updatedAt)
    {
        l2Hits.Add(1, new KeyValuePair<string, object?>("dataset", dataset));
        snapshotAgeSeconds.Record(Math.Max(0, (DateTimeOffset.UtcNow - updatedAt).TotalSeconds), new KeyValuePair<string, object?>("dataset", dataset));
    }

    public void RecordMiss(string dataset) => misses.Add(1, new KeyValuePair<string, object?>("dataset", dataset));
    public void RecordRefresh(string dataset) => refreshes.Add(1, new KeyValuePair<string, object?>("dataset", dataset));
    public void RecordSyncDuration(string dataset, double durationMs) => syncDurationMs.Record(durationMs, new KeyValuePair<string, object?>("dataset", dataset));

    public void Dispose() => meter.Dispose();
}
