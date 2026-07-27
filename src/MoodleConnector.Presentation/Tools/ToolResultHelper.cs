using System.Text.Json;
using ModelContextProtocol.Protocol;
using MoodleConnector.Application.MoodleApi;
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
    internal static CallToolResult Error<T>(string message, string status = "error", string? errorCode = null)
    {
        var response = new ToolResponse<T>(
            status,
            Data: default,
            Warnings: [message],
            AuditId: null,
            DateTimeOffset.UtcNow,
            errorCode);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }

    internal static CallToolResult Error<T>(MoodleApiException error) =>
        Error<T>(GetSafeMessage(error.ErrorCode), errorCode: error.ErrorCode);

    private static string GetSafeMessage(string errorCode) => errorCode switch
    {
        "invalid_token" => "A credencial Moodle foi recusada. Atualize a conexão e tente novamente.",
        "function_not_available" or "function_not_discovered" => "A função solicitada não está disponível nesta conexão Moodle.",
        "destructive_function_blocked" => "A função solicitada é destrutiva e está bloqueada.",
        "function_not_read_safe" or "function_not_allowed" => "A função solicitada não está autorizada para esta operação.",
        "moodle_unavailable" or "timeout" => "O Moodle está indisponível no momento. Tente novamente.",
        "moodle_invalid_response" or "moodle_empty_response" => "O Moodle retornou uma resposta inválida.",
        _ => "A chamada ao Moodle não pôde ser concluída com segurança."
    };
}
