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
    public async Task GetAccessTokenAsync_IsolaCachePorClienteMesmoComIdECredenciaisIguais()
    {
        var handler = Handler.Json("""{"token":"tenant-token"}""");
        var sut = CreateSut(handler);

        await sut.GetAccessTokenAsync(Connection(clientId: "client-a"), CancellationToken.None);
        await sut.GetAccessTokenAsync(Connection(clientId: "client-b"), CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Invalidate_RemoveSomenteOTokenDaConexao()
    {
        var handler = Handler.Json("""{"token":"renewed-token"}""");
        var sut = CreateSut(handler);
        var connection = Connection();

        await sut.GetAccessTokenAsync(connection, CancellationToken.None);
        sut.Invalidate(connection);
        await sut.GetAccessTokenAsync(connection, CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RevalidaEndpointAntesDeUsarTokenEmCache()
    {
        var handler = Handler.Json("""{"token":"cached-token"}""");
        var validator = new SequenceEndpointValidator();
        var sut = CreateSut(handler, endpointValidator: validator);

        await sut.GetAccessTokenAsync(Connection(), CancellationToken.None);
        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetAccessTokenAsync(Connection(), CancellationToken.None));

        Assert.Equal(MoodleIntegrationStage.UrlValidation, error.Stage);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(2, validator.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_UsaEndpointCanonicoProduzidoPeloValidador()
    {
        Uri? requestedUri = null;
        var handler = new Handler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"token":"token"}""", Encoding.UTF8, "application/json")
            });
        });
        var sut = CreateSut(
            handler,
            endpointValidator: new FixedEndpointValidator("https://validated.example/moodle"));

        await sut.GetAccessTokenAsync(Connection(), CancellationToken.None);

        Assert.Equal("validated.example", requestedUri?.Host);
        Assert.Equal("/moodle/login/token.php", requestedUri?.AbsolutePath);
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
    public async Task GetAccessTokenAsync_ClassificaRedirectSemArmazenarToken()
    {
        var handler = Handler.Json("{}", HttpStatusCode.TemporaryRedirect);
        var sut = CreateSut(handler);

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetAccessTokenAsync(Connection(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.ApiError, error.ErrorCode);
        Assert.Equal(307, error.HttpStatusCode);
        Assert.Equal(1, handler.Calls);
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

    private static MoodleAccessTokenProvider CreateSut(
        Handler handler,
        IMemoryCache? cache = null,
        IMoodleEndpointValidator? endpointValidator = null)
    {
        return new MoodleAccessTokenProvider(
            new HttpClient(handler),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new MoodleApiOptions()),
            Options.Create(new ConnectorSecretsOptions { TokenCacheMinutes = 20 }),
            endpointValidator ?? new FixedEndpointValidator("https://ead.fieg.com.br"),
            NullLogger<MoodleAccessTokenProvider>.Instance);
    }

    private static MoodleConnectorCredentials Connection(
        string password = "password",
        string clientId = "client") => new(
        clientId,
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

    private sealed class FixedEndpointValidator(string endpoint) : IMoodleEndpointValidator
    {
        public Task<Uri> ValidateAsync(string baseUrl, CancellationToken cancellationToken) =>
            Task.FromResult(new Uri(endpoint));
    }

    private sealed class SequenceEndpointValidator : IMoodleEndpointValidator
    {
        public int Calls { get; private set; }

        public Task<Uri> ValidateAsync(string baseUrl, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                return Task.FromResult(new Uri(baseUrl));
            }

            throw new MoodleApiException(
                MoodleErrorContract.NetworkError,
                "Endpoint no longer resolves publicly.",
                stage: MoodleIntegrationStage.UrlValidation);
        }
    }
}
