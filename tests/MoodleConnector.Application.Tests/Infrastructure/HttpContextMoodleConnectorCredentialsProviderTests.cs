using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class HttpContextMoodleConnectorCredentialsProviderTests
{
    [Fact]
    public async Task GetCurrentCredentialsAsync_ResolveAliasLegadoComAcento()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection(alias: "Goiás"));
        await db.SaveChangesAsync();

        var credentials = await CreateSut(db, "goias").GetCurrentCredentialsAsync(CancellationToken.None);

        Assert.Equal("goias", credentials.Alias);
        Assert.Equal("goias-connection", credentials.ConnectionId);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_ResolveAliasIgnorandoCaixaEEspacos()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection(alias: "goias"));
        await db.SaveChangesAsync();

        var credentials = await CreateSut(db, "  GOIÁS  ").GetCurrentCredentialsAsync(CancellationToken.None);

        Assert.Equal("goias", credentials.Alias);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_DistingueAliasInexistente()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection());
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            CreateSut(db, "senai").GetCurrentCredentialsAsync(CancellationToken.None));

        Assert.Equal(MoodleErrorContract.ConnectionNotFound, error.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(error.AuditId));
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_DistingueConexaoDesativada()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection(active: false));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            CreateSut(db, "goias").GetCurrentCredentialsAsync(CancellationToken.None));

        Assert.Equal(MoodleErrorContract.ConnectionDisabled, error.ErrorCode);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_SemAliasUsaSomenteDefaultExplicito()
    {
        await using var db = CreateDb();
        db.ConnectorClients.AddRange(
            Connection(alias: "senai", isDefault: false, id: "senai-connection"),
            Connection(alias: "goias", isDefault: true));
        await db.SaveChangesAsync();

        var credentials = await CreateSut(db, null).GetCurrentCredentialsAsync(CancellationToken.None);

        Assert.Equal("goias", credentials.Alias);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_SemAliasNaoEscolhePrimeiraAtiva()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection(isDefault: false));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            CreateSut(db, null).GetCurrentCredentialsAsync(CancellationToken.None));

        Assert.Equal(MoodleErrorContract.DefaultConnectionNotConfigured, error.ErrorCode);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_IsolaConexoesDeOutroCliente()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection(clientId: "other-client"));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            CreateSut(db, "goias", clientId: "client").GetCurrentCredentialsAsync(CancellationToken.None));

        Assert.Equal(MoodleErrorContract.ConnectionNotFound, error.ErrorCode);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_ClassificaFalhaDeDescriptografia()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection());
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            CreateSut(db, "goias", protector: new ThrowingProtector())
                .GetCurrentCredentialsAsync(CancellationToken.None));

        Assert.Equal(MoodleErrorContract.TokenDecryptionFailed, error.ErrorCode);
        Assert.Equal("goias-connection", error.ConnectionId);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_ClassificaUrlInvalidaSemExporSegredo()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection(baseUrl: "not-a-url"));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            CreateSut(db, "goias").GetCurrentCredentialsAsync(CancellationToken.None));

        Assert.Equal(MoodleErrorContract.NetworkError, error.ErrorCode);
        Assert.DoesNotContain("password", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_PreservaContextoDaConexaoQuandoDnsFalha()
    {
        await using var db = CreateDb();
        db.ConnectorClients.Add(Connection());
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            CreateSut(db, "goias", endpointValidator: new RejectEndpointValidator())
                .GetCurrentCredentialsAsync(CancellationToken.None));

        Assert.Equal(MoodleErrorContract.NetworkError, error.ErrorCode);
        Assert.Equal(MoodleIntegrationStage.UrlValidation, error.Stage);
        Assert.Equal("goias-connection", error.ConnectionId);
        Assert.Equal("goias", error.ConnectionAlias);
        Assert.Equal("https://ead.fieg.com.br", error.Endpoint);
    }

    [Fact]
    public async Task GetCurrentCredentialsAsync_PrefereConnectorClientIdPersistidoDaConta()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.UserAccounts.Add(new UserAccountEntity
        {
            Id = userId,
            Name = "Teacher",
            Email = "teacher@example.com",
            PasswordHash = "hash",
            ConnectorClientId = "persisted-client"
        });
        db.ConnectorClients.Add(Connection(clientId: "persisted-client"));
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", userId.ToString()),
            new Claim("connector_client_id", "stale-client")
        ], "test"));
        var credentials = await CreateSut(db, "goias", principal: principal)
            .GetCurrentCredentialsAsync(CancellationToken.None);

        Assert.Equal("persisted-client", credentials.ClientId);
    }

    private static ConnectorDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ConnectorDbContext(options);
    }

    private static HttpContextMoodleConnectorCredentialsProvider CreateSut(
        ConnectorDbContext db,
        string? alias,
        string clientId = "client",
        IConnectorSecretProtector? protector = null,
        ClaimsPrincipal? principal = null,
        IMoodleEndpointValidator? endpointValidator = null)
    {
        principal ??= new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("connector_client_id", clientId),
            new Claim("sub", clientId)
        ], "test"));
        var context = new DefaultHttpContext { User = principal };
        return new HttpContextMoodleConnectorCredentialsProvider(
            new HttpContextAccessor { HttpContext = context },
            db,
            protector ?? new PassThroughProtector(),
            new MoodleConnectionSelection { Alias = alias },
            endpointValidator ?? new AllowEndpointValidator(),
            NullLogger<HttpContextMoodleConnectorCredentialsProvider>.Instance);
    }

    private static ConnectorClientCredentialEntity Connection(
        string alias = "goias",
        string clientId = "client",
        bool active = true,
        bool isDefault = true,
        string id = "goias-connection",
        string baseUrl = "https://ead.fieg.com.br") => new()
    {
        Id = id,
        ClientId = clientId,
        MoodleAlias = alias,
        MoodleBaseUrl = baseUrl,
        MoodleUsernameEncrypted = "user",
        MoodlePasswordEncrypted = "password",
        MoodleTarget = alias,
        IsDefault = isDefault,
        IsActive = active
    };

    private sealed class PassThroughProtector : IConnectorSecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class ThrowingProtector : IConnectorSecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => throw new CryptographicException("bad ciphertext");
    }

    private sealed class AllowEndpointValidator : IMoodleEndpointValidator
    {
        public Task<Uri> ValidateAsync(string baseUrl, CancellationToken cancellationToken) =>
            Task.FromResult(new Uri(baseUrl));
    }

    private sealed class RejectEndpointValidator : IMoodleEndpointValidator
    {
        public Task<Uri> ValidateAsync(string baseUrl, CancellationToken cancellationToken) =>
            Task.FromException<Uri>(new MoodleApiException(
                MoodleErrorContract.NetworkError,
                "DNS failed.",
                stage: MoodleIntegrationStage.UrlValidation));
    }
}
