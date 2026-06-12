namespace MoodleConnector.Infrastructure;

internal interface IMoodleAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}