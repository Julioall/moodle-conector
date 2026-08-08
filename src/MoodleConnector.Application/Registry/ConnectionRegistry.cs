using MoodleConnector.Domain.Registry;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Registry;

public sealed class ConnectionRegistry : IConnectionRegistry
{
    private readonly IMoodleConnectionSelection _moodleSelection;

    public ConnectionRegistry(IMoodleConnectionSelection moodleSelection)
    {
        _moodleSelection = moodleSelection;
    }

    public Task<ConnectionInfo?> ResolveConnectionAsync(string? alias, CancellationToken cancellationToken = default)
    {
        // For now, we fallback to the global MoodleSelection if alias is not provided.
        // In a real implementation, we would validate against a configured list of known connections.
        var finalAlias = string.IsNullOrWhiteSpace(alias) ? _moodleSelection.Alias : alias;
        
        // Mocking a successful resolution
        var connectionId = Guid.NewGuid(); // Ideally mapped deterministically or from DB
        var connectionInfo = new ConnectionInfo(connectionId, finalAlias ?? "default", "https://mock.moodle.url");

        return Task.FromResult<ConnectionInfo?>(connectionInfo);
    }
}
