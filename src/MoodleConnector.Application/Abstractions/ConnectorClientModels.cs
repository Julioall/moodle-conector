namespace MoodleConnector.Application.Abstractions;

public sealed record ConnectorClientContext(
    string ClientId,
    bool CanWrite);

public sealed record MoodleConnectorCredentials(
    string ClientId,
    string ConnectionId,
    string Alias,
    string BaseUrl,
    string Username,
    string Password,
    string MoodleTarget,
    bool CanWrite);

public interface IMcpConnectorClientResolver
{
    Task<ConnectorClientContext?> ResolveByApiKeyAsync(string apiKey, CancellationToken cancellationToken);
}

public interface IMoodleConnectorCredentialsProvider
{
    Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken);
}

public interface IMoodleConnectionSelection
{
    string? Alias { get; set; }
}
