using System.Text.Json;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal static class MoodleResponseParser
{
    public static JsonElement Parse(string payload, bool allowEmptyResponse = false)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            if (allowEmptyResponse)
            {
                using var emptyDocument = JsonDocument.Parse("null");
                return emptyDocument.RootElement.Clone();
            }

            throw new MoodleApiException(
                MoodleErrorContract.InvalidResponse,
                "Moodle returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && IsMoodleErrorEnvelope(root, out var errorCode))
            {
                throw new MoodleApiException(
                    string.IsNullOrWhiteSpace(errorCode) ? MoodleErrorContract.ApiError : errorCode,
                    "Moodle returned a structured Web Service error.",
                    remoteErrorCode: errorCode);
            }

            return root.Clone();
        }
        catch (JsonException ex)
        {
            throw new MoodleApiException(
                MoodleErrorContract.InvalidResponse,
                "Moodle returned invalid JSON.",
                innerException: ex);
        }
    }

    private static bool IsMoodleErrorEnvelope(JsonElement root, out string? errorCode)
    {
        errorCode = null;
        var hasException = root.TryGetProperty("exception", out _);
        var hasError = root.TryGetProperty("error", out var errorElement) &&
            errorElement.ValueKind == JsonValueKind.String;
        if (!hasException && !hasError)
        {
            return false;
        }

        if (root.TryGetProperty("errorcode", out var errorCodeElement) &&
            errorCodeElement.ValueKind == JsonValueKind.String)
        {
            errorCode = errorCodeElement.GetString();
        }

        return true;
    }
}
