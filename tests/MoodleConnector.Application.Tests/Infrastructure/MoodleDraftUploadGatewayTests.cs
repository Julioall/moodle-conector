using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleDraftUploadGatewayTests
{
    [Fact]
    public async Task UploadAsync_EnviaMultipartParaAreaDeRascunhoERetornaItemId()
    {
        var handler = new Handler();
        var sut = new MoodleDraftUploadGateway(new HttpClient(handler), new TokenProvider());
        var connection = new MoodleConnectorCredentials(
            "client", "connection", "goias", "https://moodle.example", "user", "password", "goias", true);

        var result = await sut.UploadAsync(
            connection,
            new MoodleDraftUploadRequest("resposta.txt", "text/plain", Encoding.UTF8.GetBytes("ola"), "/", 7),
            CancellationToken.None);

        var file = Assert.Single(result.Files);
        Assert.Equal(42, file.ItemId);
        Assert.Equal("resposta.txt", file.Filename);
        Assert.Contains("/webservice/upload.php", handler.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("itemid", handler.Body, StringComparison.Ordinal);
        Assert.Contains("resposta.txt", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_RejeitaEnvelopeDeErroMoodleEmHttp200EInvalidaToken()
    {
        var handler = new Handler("{\"error\":\"Token inválido\",\"errorcode\":\"invalidtoken\"}");
        var tokens = new TokenProvider();
        var sut = new MoodleDraftUploadGateway(new HttpClient(handler), tokens);
        var connection = new MoodleConnectorCredentials(
            "client", "connection", "goias", "https://moodle.example", "user", "password", "goias", true);

        var exception = await Assert.ThrowsAsync<MoodleApiException>(() => sut.UploadAsync(
            connection,
            new MoodleDraftUploadRequest("resposta.txt", "text/plain", Encoding.UTF8.GetBytes("ola"), "/", 7),
            CancellationToken.None));

        Assert.Equal("invalidtoken", exception.ErrorCode);
        Assert.Equal(1, tokens.Invalidations);
    }

    [Fact]
    public async Task UploadAsync_RejeitaRespostaBemSucedidaSemArquivo()
    {
        var handler = new Handler("[]");
        var sut = new MoodleDraftUploadGateway(new HttpClient(handler), new TokenProvider());
        var connection = new MoodleConnectorCredentials(
            "client", "connection", "goias", "https://moodle.example", "user", "password", "goias", true);

        var exception = await Assert.ThrowsAsync<MoodleApiException>(() => sut.UploadAsync(
            connection,
            new MoodleDraftUploadRequest("resposta.txt", "text/plain", Encoding.UTF8.GetBytes("ola"), "/", 7),
            CancellationToken.None));

        Assert.Equal(MoodleErrorContract.InvalidResponse, exception.ErrorCode);
    }

    private sealed class Handler(string responseBody = "[{\"itemid\":42,\"filename\":\"resposta.txt\",\"filepath\":\"/\",\"filesize\":3,\"mimetype\":\"text/plain\"}]") : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        }
    }

    private sealed class TokenProvider : IMoodleAccessTokenProvider
    {
        public int Invalidations { get; private set; }

        public Task<string> GetAccessTokenAsync(MoodleConnectorCredentials connection, CancellationToken cancellationToken) =>
            Task.FromResult("user-token");

        public void Invalidate(MoodleConnectorCredentials connection)
        {
            Invalidations++;
        }
    }
}
