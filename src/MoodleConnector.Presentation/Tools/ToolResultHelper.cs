using System.Text.Json;
using ModelContextProtocol.Protocol;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools;

/// <summary>
/// Helpers estáticos compartilhados por todas as MCP tool classes da camada de Apresentação.
/// </summary>
internal static class ToolResultHelper
{
    /// <summary>
    /// Cria um <see cref="CallToolResult"/> de erro tipado no formato padrão <see cref="ToolResponse{T}"/>.
    /// </summary>
    internal static CallToolResult Error<T>(string message, string status = "error")
    {
        var response = new ToolResponse<T>(
            status,
            Data: default,
            Warnings: [message],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }
}
