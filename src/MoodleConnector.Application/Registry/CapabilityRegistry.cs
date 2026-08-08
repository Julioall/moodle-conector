using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly IMoodleRestClient _restClient;
    private readonly ConcurrentDictionary<string, CapabilitySnapshot> _cache = new();

    public CapabilityRegistry(IMoodleRestClient restClient)
    {
        _restClient = restClient;
    }

    public async Task<CapabilitySnapshot> GetSnapshotAsync(ConnectionInfo connectionInfo, string userToken, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{connectionInfo.ConnectionId}:{userToken}";
        
        if (_cache.TryGetValue(cacheKey, out var snapshot) && (DateTimeOffset.UtcNow - snapshot.CapturedAt).TotalMinutes < 60)
        {
            return snapshot;
        }

        // Fetch from real Moodle
        var credentials = new MoodleConnectorCredentials("internal", connectionInfo.ConnectionId.ToString(), connectionInfo.Alias, connectionInfo.BaseUrl, userToken, "unused", "moodle", false);
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
