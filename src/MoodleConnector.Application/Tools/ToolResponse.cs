using System.Text.Json.Serialization;

namespace MoodleConnector.Application.Tools;

public sealed record ToolResponse<T>(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("auditId")] string? AuditId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null,
    [property: JsonPropertyName("message")] string? Message = null);
