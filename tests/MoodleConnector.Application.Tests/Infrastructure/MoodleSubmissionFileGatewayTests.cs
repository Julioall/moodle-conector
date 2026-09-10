using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleSubmissionFileGatewayTests
{
    [Fact]
    public async Task DownloadFileAsync_UsaTokenNaQueryESubstituiTokenAnterior()
    {
        var handler = new Handler();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider(),
            new CredentialsProvider());

        await sut.DownloadFileAsync("1", "https://moodle.example/pluginfile.php/1/a.pdf?token=old&x=1", "a.pdf", 1000, CancellationToken.None);

        Assert.Null(handler.AuthorizationScheme);
        Assert.Null(handler.AuthorizationParameter);
        Assert.DoesNotContain("token=old", handler.Uri!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("token=new-token", handler.Uri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("x=1", handler.Uri.Query);
    }

    [Fact]
    public async Task DownloadFileAsync_CodificaTokenAntesDeAnexaLoNaQuery()
    {
        var handler = new Handler();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider("token-with&injected=parameter"),
            new CredentialsProvider());

        await sut.DownloadFileAsync(
            "1",
            "https://moodle.example/pluginfile.php/1/a.pdf",
            "a.pdf",
            1000,
            CancellationToken.None);

        Assert.Contains("token=token-with%26injected%3Dparameter", handler.Uri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("&injected=parameter", handler.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadFileAsync_InvalidaTokenEmHttp401()
    {
        var handler = new Handler(HttpStatusCode.Unauthorized);
        var tokens = new TokenProvider();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            tokens,
            new CredentialsProvider());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.DownloadFileAsync(
                "1",
                "https://moodle.example/pluginfile.php/1/a.pdf",
                "a.pdf",
                1000,
                CancellationToken.None));

        Assert.Equal(1, tokens.Invalidations);
    }

    [Fact]
    public async Task DownloadFileAsync_AceitaEndpointWebservicePluginfile()
    {
        var handler = new Handler();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider(),
            new CredentialsProvider());

        var result = await sut.DownloadFileAsync(
            "1",
            "https://moodle.example/webservice/pluginfile.php/1/a.pdf",
            "a.pdf",
            1000,
            CancellationToken.None);

        Assert.Equal(3, result.SizeBytes);
        Assert.Contains("/webservice/pluginfile.php/", handler.Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadFileAsync_RejeitaEnvelopeDeErroMoodleEmHttp200EInvalidaToken()
    {
        var handler = new Handler(
            contentType: "application/json",
            body: "{\"error\":\"Token inválido\",\"errorcode\":\"invalidtoken\"}");
        var tokens = new TokenProvider();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            tokens,
            new CredentialsProvider());

        var exception = await Assert.ThrowsAsync<MoodleApiException>(() => sut.DownloadFileAsync(
            "1",
            "https://moodle.example/webservice/pluginfile.php/1/a.json",
            "a.json",
            1000,
            CancellationToken.None));

        Assert.Equal("invalidtoken", exception.ErrorCode);
        Assert.Equal(1, tokens.Invalidations);
    }

    [Fact]
    public async Task DownloadFileAsync_PreservaArquivoJsonLegitimo()
    {
        const string json = "{\"answer\":42}";
        var handler = new Handler(contentType: "application/json", body: json);
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider(),
            new CredentialsProvider());

        var result = await sut.DownloadFileAsync(
            "1",
            "https://moodle.example/pluginfile.php/1/resultado.json",
            "resultado.json",
            1000,
            CancellationToken.None);

        Assert.Equal("application/json", result.MimeType);
        Assert.Equal(Encoding.UTF8.GetByteCount(json), result.SizeBytes);
        Assert.Equal(Encoding.UTF8.GetBytes(json), result.Content);
    }

    [Fact]
    public async Task DownloadFileAsync_ComMimeGenericoPreservaDeteccaoPorExtensaoRtf()
    {
        var handler = new Handler(contentType: "application/octet-stream");
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider(),
            new CredentialsProvider());

        var result = await sut.DownloadFileAsync(
            "1",
            "https://moodle.example/pluginfile.php/1/resposta.rtf",
            "resposta.rtf",
            1000,
            CancellationToken.None);

        Assert.Equal("text/rtf", result.MimeType);
        Assert.Equal(3, result.SizeBytes);
    }

    [Fact]
    public async Task DownloadFileAsync_RecusaPluginfileForaDoSubdiretorioDaConexao()
    {
        var handler = new Handler();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider(),
            new CredentialsProvider("https://moodle.example/moodle"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DownloadFileAsync(
            "1",
            "https://moodle.example/outro/pluginfile.php/1/a.pdf",
            "a.pdf",
            1000,
            CancellationToken.None));

        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task DownloadFileAsync_AceitaPluginfileNoSubdiretorioDaConexao()
    {
        var handler = new Handler();
        var sut = new MoodleSubmissionFileGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new TokenProvider(),
            new CredentialsProvider("https://moodle.example/moodle"));

        await sut.DownloadFileAsync(
            "1",
            "https://moodle.example/moodle/pluginfile.php/1/a.pdf",
            "a.pdf",
            1000,
            CancellationToken.None);

        Assert.Equal("/moodle/pluginfile.php/1/a.pdf", handler.Uri!.AbsolutePath);
    }

    private sealed class Handler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? contentType = null,
        string? body = null) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            var content = new ByteArrayContent(body is null ? [1, 2, 3] : Encoding.UTF8.GetBytes(body));
            if (contentType is not null)
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            }
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
        }
    }

    private sealed class TokenProvider(string token = "new-token") : IMoodleAccessTokenProvider
    {
        public int Invalidations { get; private set; }

        public Task<string> GetAccessTokenAsync(
            MoodleConnectorCredentials connection,
            CancellationToken cancellationToken) => Task.FromResult(token);

        public void Invalidate(MoodleConnectorCredentials connection)
        {
            Invalidations++;
        }
    }

    private sealed class CredentialsProvider(string baseUrl = "https://moodle.example") : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) => Task.FromResult(
            new MoodleConnectorCredentials("c", "id", "goias", baseUrl, "u", "p", "goias", false));
    }
}
