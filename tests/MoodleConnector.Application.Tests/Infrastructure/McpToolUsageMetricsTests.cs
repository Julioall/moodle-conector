using System.Diagnostics.Metrics;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class McpToolUsageMetricsTests
{
    [Fact]
    public void Records_low_cardinality_tool_tags_without_payloads()
    {
        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "MoodleConnector.McpTools")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(item => item.Key, item => item.Value))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(item => item.Key, item => item.Value))));
        listener.Start();

        using var metrics = new McpToolUsageMetrics();
        metrics.RecordInvocation(
            "get_submission_status",
            "assignments.submissions.get_student",
            "get_student_submission",
            "Production",
            "success",
            null,
            12.5);
        metrics.RecordInvocation(
            "not a valid tool with token=secret",
            "operation with payload",
            null,
            "Production",
            "denied",
            "permission denied",
            1);

        var invocation = measurements.Single(item => item.Name == "mcp_tool_invocations" && item.Tags["tool"]?.ToString() == "get_submission_status");
        Assert.Equal("assignments.submissions.get_student", invocation.Tags["canonical_operation"]);
        Assert.Equal("get_student_submission", invocation.Tags["compatibility_alias_of"]);
        Assert.Equal("success", invocation.Tags["outcome"]);

        var sanitized = measurements.Single(item => item.Name == "mcp_tool_invocations" && item.Tags["outcome"]?.ToString() == "denied");
        Assert.Equal("other", sanitized.Tags["tool"]);
        Assert.Equal("other", sanitized.Tags["canonical_operation"]);
        Assert.DoesNotContain("secret", string.Join('|', sanitized.Tags.Values));
        Assert.Contains(measurements, item => item.Name == "mcp_tool_failures" && item.Tags["outcome"]?.ToString() == "denied");
    }
}
