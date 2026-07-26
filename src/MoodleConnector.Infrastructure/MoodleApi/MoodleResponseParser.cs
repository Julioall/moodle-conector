using System.Text.Json;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal static class MoodleResponseParser
{
    public static JsonElement Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new MoodleApiException("moodle_empty_response", "O Moodle retornou uma resposta vazia.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("exception", out _))
            {
                var errorCode = root.TryGetProperty("errorcode", out var errorCodeElement)
                    ? errorCodeElement.GetString()
                    : null;
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                throw new MoodleApiException(
                    string.IsNullOrWhiteSpace(errorCode) ? "moodle_error" : errorCode,
                    string.IsNullOrWhiteSpace(message) ? "O Moodle recusou a chamada solicitada." : message);
            }

            return root.Clone();
        }
        catch (JsonException ex)
        {
            throw new MoodleApiException("moodle_invalid_response", "O Moodle retornou uma resposta JSON invalida.", null) { Source = ex.Source };
        }
    }
}
