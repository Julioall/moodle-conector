using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleAccessTokenProviderTests
{
    [Fact]
    public async Task GetAccessTokenAsync_RetornaTokenSemExpoLo()
    {
        var handler = Handler.Json("""{"token":"secret-token"}""");
        var sut = CreateSut(handler);

        var token = await sut.GetAccessTokenAsync(Connection(), CancellationToken.None);

        Assert.Equal("secret-token", token);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_UsaCacheParaMesmaConexaoECredencial()
    {
        var handler = Handler.Json("""{"token":"cached-token"}""");
        var sut = CreateSut(handler);

        await sut.GetAccessTokenAsync(Connection(), CancellationToken.None);
        await sut.GetAccessTokenAsync(Connection(), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_NaoReutilizaCacheDepoisDeTrocarSenha()
    {
        var handler = Handler.Json("""{"token":"rotated-token"}""");
        var sut = CreateSut(handler);

        await sut.GetAccessTokenAsync(Connection(password: "old-password"), CancellationToken.None);
        await sut.GetAccessTokenAsync(Connection(password: "new-password"), CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ClassificaInvalidLoginEmHttp200()
    {
        var sut = CreateSut(Handler.Json("""{"error":"Invalid login","errorcode":"invalidlogin"}"""));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetAccessTokenAsync(Connection(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.AuthenticationFailed, error.ErrorCode);
        Assert.Equal("invalidlogin", error.RemoteErrorCode);
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ClassificaHttp401()
    {
        var sut = CreateSut(Handler.Json(
            """{"error":"Unauthorized","errorcode":"invalidtoken"}""",
            HttpStatusCode.Unauthorized));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetAccessTokenAsync(Connection(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.AuthenticationFailed, error.ErrorCode);
        Assert.Equal(401, error.HttpStatusCode);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ClassificaRespostaSemToken()
    {
        var sut = CreateSut(Handler.Json("{}"));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetAccessTokenAsync(Connection(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.TokenMissing, error.ErrorCode);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ClassificaJsonInvalido()
    {
        var sut = CreateSut(Handler.Json("<html>not-json</html>"));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetAccessTokenAsync(Connection(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.InvalidResponse, error.ErrorCode);
    }

    [Fact]
    public async Task GetAccessTokenAsync_DiferenciaTimeoutDeCancelamentoDoChamador()
    {
        var sut = CreateSut(new Handler((_, _) => throw new TaskCanceledException("timeout")));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetAccessTokenAsync(Connection(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.RequestTimeout, error.ErrorCode);
    }

    private static MoodleAccessTokenProvider CreateSut(Handler handler)
    {
        return new MoodleAccessTokenProvider(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new MoodleApiOptions()),
            Options.Create(new ConnectorSecretsOptions { TokenCacheMinutes = 20 }),
            NullLogger<MoodleAccessTokenProvider>.Instance);
    }

    private static MoodleConnectorCredentials Connection(string password = "password") => new(
        "client",
        "goias-connection",
        "goias",
        "https://ead.fieg.com.br",
        "user",
        password,
        "goias",
        false);

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public static Handler Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return response(request, cancellationToken);
        }
    }
}
