using System.Security.Cryptography;
using System.Text;
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
        // A rotated Moodle credential must not inherit the capabilities discovered
        // with its predecessor.
        var cacheKey = $"moodle:function-profile:{connection.ConnectionId}:{CreateCredentialFingerprint(connection)}";
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
                allowServiceToken: false,
                cancellationToken);

            return MoodleFunctionProfileParser.Parse(connection, payload);
        }) ?? throw new MoodleApiException("moodle_profile_unavailable", "Nao foi possivel criar o perfil de funcoes Moodle.");
    }

    private static string CreateCredentialFingerprint(MoodleConnectorCredentials connection)
    {
        var value = $"{connection.BaseUrl}\u001f{connection.Username}\u001f{connection.Password}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}
