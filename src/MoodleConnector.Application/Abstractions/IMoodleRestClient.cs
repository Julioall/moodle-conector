using System.Text.Json;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleRestClient
{
    Task<JsonElement> CallAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);

    Task<JsonElement> CallAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        bool allowServiceToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes a controlled write whose Moodle REST implementation may return
    /// an empty HTTP body on success. Read calls must keep treating an empty
    /// body as an invalid response.
    /// </summary>
    Task<JsonElement> CallWriteAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        CallAsync(connection, functionName, parameters, allowServiceToken: false, cancellationToken);
}
