using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure.MoodleApi;

public sealed class MoodleRestClientErrorContractTests
{
    [Theory]
    [InlineData("invalidtoken", MoodleErrorContract.AuthenticationFailed)]
    [InlineData("accessexception", MoodleErrorContract.PermissionDenied)]
    [InlineData("webservice_access_exception", MoodleErrorContract.PermissionDenied)]
    [InlineData("invalidcourseid", MoodleErrorContract.CourseNotFound)]
    public async Task CallAsync_NormalizaCodigosRemotosNoContrato(
        string remoteCode,
        string expectedStableCode)
    {
        var sut = CreateSut(Handler.Json(
            $$"""{"exception":"moodle_exception","errorcode":"{{remoteCode}}","message":"remote detail"}"""));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.CallAsync(Connection(), "core_webservice_get_site_info", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Equal(expectedStableCode, MoodleErrorContract.Describe(error).ErrorCode);
        Assert.Equal(remoteCode, error.RemoteErrorCode);
        Assert.Contains(remoteCode, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("remote detail", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_ClassificaHttp403SemPayloadEstruturado()
    {
        var sut = CreateSut(Handler.Text("forbidden", HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.CallAsync(Connection(), "core_webservice_get_site_info", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.PermissionDenied, error.ErrorCode);
        Assert.Equal(403, error.HttpStatusCode);
    }

    [Fact]
    public async Task CallAsync_InvalidaTokenCacheadoQuandoMoodleRetornaInvalidToken()
    {
        var tokenProvider = new TokenProvider();
        var sut = CreateSut(
            Handler.Json(
                """{"exception":"moodle_exception","errorcode":"invalidtoken","message":"invalid"}"""),
            tokenProvider);

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.CallAsync(Connection(), "core_webservice_get_site_info", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.AuthenticationFailed, error.ErrorCode);
        Assert.Equal(1, tokenProvider.Invalidations);
    }

    [Fact]
    public async Task CallAsync_ClassificaJsonInvalido()
    {
        var sut = CreateSut(Handler.Json("<html>invalid</html>"));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.CallAsync(Connection(), "core_webservice_get_site_info", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.InvalidResponse, error.ErrorCode);
    }

    [Fact]
    public async Task CallAsync_ClassificaTimeout()
    {
        var sut = CreateSut(new Handler((_, _) => throw new TaskCanceledException("timeout")));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.CallAsync(Connection(), "core_webservice_get_site_info", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.RequestTimeout, error.ErrorCode);
    }

    [Fact]
    public async Task CallAsync_ClassificaFalhaDeRede()
    {
        var sut = CreateSut(new Handler((_, _) => throw new HttpRequestException("dns failure")));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.CallAsync(Connection(), "core_webservice_get_site_info", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.NetworkError, error.ErrorCode);
    }

    [Fact]
    public async Task CallAsync_NaoEnviaServiceTokenGlobalParaOutroHost()
    {
        var tokenProvider = new TokenProvider();
        var sut = CreateSut(Handler.Json("{}"), tokenProvider);

        await sut.CallAsync(Connection(), "core_webservice_get_site_info", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.Equal(1, tokenProvider.Calls);
    }

    private static MoodleRestClient CreateSut(
        Handler handler,
        TokenProvider? tokenProvider = null) => new(
        new HttpClient(handler),
        tokenProvider ?? new TokenProvider(),
        NullLogger<MoodleRestClient>.Instance);

    private static MoodleConnectorCredentials Connection() => new(
        "client",
        "goias-connection",
        "goias",
        "https://ead.fieg.com.br",
        "user",
        "password",
        "goias",
        false);

    private sealed class TokenProvider : IMoodleAccessTokenProvider
    {
        public int Calls { get; private set; }
        public int Invalidations { get; private set; }

        public Task<string> GetAccessTokenAsync(
            MoodleConnectorCredentials connection,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("connection-token");
        }

        public void Invalidate(MoodleConnectorCredentials connection)
        {
            Invalidations++;
        }
    }

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public static Handler Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
            Text(body, statusCode, "application/json");

        public static Handler Text(
            string body,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "text/plain") =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            }));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request, cancellationToken);
    }
}
