using System.Text.Json.Serialization;

namespace MoodleConnector.Application.Tools;

public sealed record ToolFreshness(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("snapshotAt")] DateTimeOffset? SnapshotAt,
    [property: JsonPropertyName("ageSeconds")] long? AgeSeconds,
    [property: JsonPropertyName("stale")] bool Stale,
    [property: JsonPropertyName("refreshQueued")] bool RefreshQueued,
    [property: JsonPropertyName("complete")] bool Complete,
    [property: JsonPropertyName("recordCount")] int RecordCount);

public sealed record ToolResponse<T>(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("auditId")] string? AuditId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("freshness")] ToolFreshness? Freshness = null);
