using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleFunctionCatalog(
    IMemoryCache cache,
    IMoodleRestClient restClient,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleFunctionCatalog
{
    private static readonly TimeSpan ProfileCacheDuration = TimeSpan.FromMinutes(15);

    public async Task<MoodleFunctionProfile> GetCurrentAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var connection = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var cacheKey = $"moodle:function-profile:{connection.ConnectionId}";
        if (forceRefresh)
        {
            cache.Remove(cacheKey);
        }

        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ProfileCacheDuration;
            var payload = await restClient.CallAsync(
                connection,
                "core_webservice_get_site_info",
                new Dictionary<string, object?>(),
                allowServiceToken: true,
                cancellationToken);

            return CreateProfile(connection, payload);
        }) ?? throw new MoodleApiException("moodle_profile_unavailable", "Nao foi possivel criar o perfil de funcoes Moodle.");
    }

    private static MoodleFunctionProfile CreateProfile(MoodleConnectorCredentials connection, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new MoodleApiException("moodle_invalid_response", "O Moodle retornou um perfil de site invalido.");
        }

        var functions = payload.TryGetProperty("functions", out var functionsElement) && functionsElement.ValueKind == JsonValueKind.Array
            ? functionsElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out _))
                .Select(item => item.GetProperty("name").GetString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new MoodleFunctionDescriptor(name, MoodleReadFunctionPolicy.Classify(name), true))
                .ToArray()
            : [];

        return new MoodleFunctionProfile(
            connection.ConnectionId,
            connection.Alias,
            GetString(payload, "sitename"),
            GetString(payload, "release"),
            GetInt64(payload, "userid"),
            functions,
            DateTimeOffset.UtcNow);
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
