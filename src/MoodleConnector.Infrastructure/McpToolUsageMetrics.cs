using System.Diagnostics;
using System.Diagnostics.Metrics;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

/// <summary>
/// Low-cardinality MCP usage metrics intended to support evidence-based
/// exposure decisions. It deliberately records no arguments or response data.
/// </summary>
public sealed class McpToolUsageMetrics : IMcpToolUsageTelemetry, IDisposable
{
    private readonly Meter _meter = new("MoodleConnector.McpTools", "1.0.0");
    private readonly Counter<long> _invocations;
    private readonly Counter<long> _failures;
    private readonly Histogram<double> _durationMs;

    public McpToolUsageMetrics()
    {
        _invocations = _meter.CreateCounter<long>("mcp_tool_invocations");
        _failures = _meter.CreateCounter<long>("mcp_tool_failures");
        _durationMs = _meter.CreateHistogram<double>("mcp_tool_duration_ms");
    }

    public void RecordInvocation(
        string toolName,
        string? canonicalOperation,
        string? compatibilityAliasOf,
        string exposureProfile,
        string outcome,
        string? errorCode,
        double durationMs)
    {
        var tags = Tags(toolName, canonicalOperation, compatibilityAliasOf, exposureProfile, outcome);
        _invocations.Add(1, tags);
        _durationMs.Record(Math.Max(0, durationMs), tags);

        if (!string.Equals(outcome, "success", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("error_code", NormalizeTag(errorCode, "unknown"));
            _failures.Add(1, tags);
        }
    }

    public void Dispose() => _meter.Dispose();

    private static TagList Tags(
        string? toolName,
        string? canonicalOperation,
        string? compatibilityAliasOf,
        string? exposureProfile,
        string? outcome) => new()
    {
        { "tool", NormalizeTag(toolName, "unknown") },
        { "canonical_operation", NormalizeTag(canonicalOperation, "unknown") },
        { "compatibility_alias_of", NormalizeTag(compatibilityAliasOf, "none") },
        { "exposure_profile", NormalizeTag(exposureProfile, "Production") },
        { "outcome", NormalizeTag(outcome, "unknown") }
    };

    private static string NormalizeTag(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var normalized = value.Trim();
        if (normalized.Length > 120)
            return "other";

        foreach (var character in normalized)
        {
            if (!(char.IsLetterOrDigit(character) || character is '.' or '_' or '-'))
                return "other";
        }

        return normalized;
    }
}
