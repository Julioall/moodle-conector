using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly IMoodleRestClient _restClient;
    private readonly IMoodleConnectorCredentialsProvider? _credentialsProvider;
    private readonly ConcurrentDictionary<string, CapabilitySnapshot> _cache = new();

    public CapabilityRegistry(
        IMoodleRestClient restClient,
        IMoodleConnectorCredentialsProvider? credentialsProvider = null)
    {
        _restClient = restClient;
        _credentialsProvider = credentialsProvider;
    }

    public async Task<CapabilitySnapshot> GetSnapshotAsync(ConnectionInfo connectionInfo, string userToken, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{connectionInfo.ConnectionId}:{userToken}";
        
        if (_cache.TryGetValue(cacheKey, out var snapshot) && (DateTimeOffset.UtcNow - snapshot.CapturedAt).TotalMinutes < 60)
        {
            return snapshot;
        }

        var credentials = _credentialsProvider is null
            ? new MoodleConnectorCredentials(
                "internal",
                connectionInfo.ConnectionId.ToString(),
                connectionInfo.Alias,
                connectionInfo.BaseUrl,
                userToken,
                "unused",
                "moodle",
                false)
            : await _credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);

        if (!string.Equals(credentials.Alias, connectionInfo.Alias, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(credentials.BaseUrl.TrimEnd('/'), connectionInfo.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resolved Moodle connection changed before capability discovery.");
        }

        var payload = await _restClient.CallAsync(credentials, "core_webservice_get_site_info", new Dictionary<string, object?>(), true, cancellationToken);
        var node = JsonNode.Parse(payload.GetRawText());

        var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        if (node?["functions"] is JsonArray functionsArray)
        {
            foreach (var functionNode in functionsArray)
            {
                var funcName = functionNode?["name"]?.ToString();
                if (!string.IsNullOrEmpty(funcName))
                {
                    functions.Add(funcName);
                }
            }
        }

        var newSnapshot = new CapabilitySnapshot(
            connectionInfo.ConnectionId,
            userToken,
            functions,
            DateTimeOffset.UtcNow
        );

        _cache[cacheKey] = newSnapshot;
        return newSnapshot;
    }

    public void Invalidate(ConnectionInfo connectionInfo, string userToken)
    {
        var cacheKey = $"{connectionInfo.ConnectionId}:{userToken}";
        _cache.TryRemove(cacheKey, out _);
    }
}
