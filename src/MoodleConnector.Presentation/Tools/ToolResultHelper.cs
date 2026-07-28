using System.Text.Json;
using ModelContextProtocol.Protocol;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools;

/// <summary>
/// Shared MCP result factory. Every failure receives a stable code and correlation id.
/// </summary>
internal static class ToolResultHelper
{
    internal static CallToolResult Error<T>(
        string message,
        string status = "error",
        string? errorCode = null,
        string? auditId = null)
    {
        var normalizedCode = MoodleErrorContract.NormalizeCode(errorCode ?? MoodleErrorContract.Unexpected);
        var correlationId = string.IsNullOrWhiteSpace(auditId)
            ? Guid.NewGuid().ToString("N")
            : auditId;
        var response = new ToolResponse<T>(
            status,
            Data: default,
            Warnings: [message],
            AuditId: correlationId,
            DateTimeOffset.UtcNow,
            normalizedCode,
            message);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }

    internal static CallToolResult Error<T>(MoodleApiException error) =>
        Error<T>(
            MoodleErrorContract.SafeMessage(error.ErrorCode),
            errorCode: error.ErrorCode,
            auditId: error.AuditId);

    internal static CallToolResult Error<T>(Exception error)
    {
        var descriptor = MoodleErrorContract.Describe(error);
        return Error<T>(
            descriptor.Message,
            errorCode: descriptor.ErrorCode,
            auditId: descriptor.AuditId);
    }
}
