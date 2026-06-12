using System.Text.Json;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleProxyGateway
{
    Task<JsonElement> GetHealthAsync(CancellationToken cancellationToken);

    Task<JsonElement> GetSessionStatusAsync(CancellationToken cancellationToken);
}