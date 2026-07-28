using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal interface IMoodleAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(
        MoodleConnectorCredentials connection,
        CancellationToken cancellationToken);
}
