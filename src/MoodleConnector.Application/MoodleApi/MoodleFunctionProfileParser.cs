using System.Text.Json;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.MoodleApi;

public static class MoodleFunctionProfileParser
{
    public static MoodleFunctionProfile Parse(
        MoodleConnectorCredentials connection,
        JsonElement payload,
        DateTimeOffset? discoveredAt = null)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new MoodleApiException(
                MoodleErrorContract.InvalidResponse,
                "Moodle returned an invalid site profile.",
                connectionId: connection.ConnectionId,
                connectionAlias: connection.Alias,
                functionName: "core_webservice_get_site_info",
                stage: MoodleIntegrationStage.ResponseParsing);
        }

        var functions = payload.TryGetProperty("functions", out var functionsElement) &&
                        functionsElement.ValueKind == JsonValueKind.Array
            ? functionsElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out _))
                .Select(item => item.GetProperty("name").GetString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new MoodleFunctionDescriptor(
                    name,
                    MoodleReadFunctionPolicy.Classify(name),
                    true))
                .ToArray()
            : [];

        return new MoodleFunctionProfile(
            connection.ConnectionId,
            connection.Alias,
            GetString(payload, "sitename"),
            GetString(payload, "release"),
            GetInt64(payload, "userid"),
            functions,
            discoveredAt ?? DateTimeOffset.UtcNow);
    }

    private static string? GetString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetInt64(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : null;
}
