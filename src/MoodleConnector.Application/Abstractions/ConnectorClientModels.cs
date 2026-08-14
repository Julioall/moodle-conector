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

/// <summary>
/// Contexto explícito usado por workers internos quando não existe uma
/// requisição HTTP para fornecer o cliente, o ator e os escopos atuais.
/// </summary>
public interface IConnectorExecutionContext
{
    string? ClientId { get; }
    string? Subject { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Scopes { get; }

    void Enter(string clientId, string subject, string? email, IReadOnlyCollection<string>? scopes = null);
    void Clear();
}

public interface IMoodleConnectionSelection
{
    string? Alias { get; set; }
}
