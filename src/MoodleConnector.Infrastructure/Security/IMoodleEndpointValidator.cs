namespace MoodleConnector.Infrastructure;

internal interface IMoodleEndpointValidator
{
    Task<Uri> ValidateAsync(string baseUrl, CancellationToken cancellationToken);
}
