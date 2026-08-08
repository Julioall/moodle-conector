using System.Threading;
using System.Threading.Tasks;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Benchmarks;

public class FakeConnectorClientResolver : IMcpConnectorClientResolver
{
    public Task<ConnectorClientContext?> ResolveByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ConnectorClientContext?>(new ConnectorClientContext("test-client-id", true));
    }
}
