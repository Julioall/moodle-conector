using System.Net;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleSubmissionFileGatewayTests
{
    [Fact]
    public async Task DownloadFileAsync_UsaBearerESanitizaTokenDaUrl()
    {
        var handler = new Handler();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider(),
            new CredentialsProvider());

        await sut.DownloadFileAsync("1", "https://moodle.example/pluginfile.php/1/a.pdf?token=old&x=1", "a.pdf", 1000, CancellationToken.None);

        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("new-token", handler.AuthorizationParameter);
        Assert.DoesNotContain("token", handler.Uri!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x=1", handler.Uri.Query);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private sealed class TokenProvider : IMoodleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            MoodleConnectorCredentials connection,
            CancellationToken cancellationToken) => Task.FromResult("new-token");
    }

    private sealed class CredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) => Task.FromResult(
            new MoodleConnectorCredentials("c", "id", "goias", "https://moodle.example", "u", "p", "goias", false));
    }
}
