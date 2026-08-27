namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Records aggregate MCP tool usage without retaining arguments, payloads,
/// credentials, user identifiers or other potentially sensitive data.
/// </summary>
public interface IMcpToolUsageTelemetry
{
    void RecordInvocation(
        string toolName,
        string? canonicalOperation,
        string? compatibilityAliasOf,
        string exposureProfile,
        string outcome,
        string? errorCode,
        double durationMs);
}
