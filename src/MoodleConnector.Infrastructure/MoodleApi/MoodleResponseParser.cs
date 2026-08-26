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
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("exception", out _))
            {
                var errorCode = root.TryGetProperty("errorcode", out var errorCodeElement)
                    ? errorCodeElement.GetString()
                    : null;
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
}
