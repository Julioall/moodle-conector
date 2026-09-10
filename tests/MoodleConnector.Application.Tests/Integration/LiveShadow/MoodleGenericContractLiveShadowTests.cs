using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Registry;
using MoodleConnector.Domain.Registry;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.MoodleApi;
using Xunit;

namespace MoodleConnector.Application.Tests.Integration.LiveShadow;

[Trait("Category", "LiveShadow")]
public sealed class MoodleGenericContractLiveShadowTests
{
    [Theory]
    [InlineData("fieg")]
    [InlineData("senai")]
    public async Task VerifiedContract_ExecutesRealReadForTheDiscoveredRelease(string alias)
    {
        var prefix = alias.ToUpperInvariant();
        var accessToken = Environment.GetEnvironmentVariable($"LIVE_{prefix}_TOKEN");
        var username = Environment.GetEnvironmentVariable($"LIVE_{prefix}_USERNAME");
        var password = Environment.GetEnvironmentVariable($"LIVE_{prefix}_PASSWORD");
        var baseUrl = Environment.GetEnvironmentVariable($"LIVE_{prefix}_URL");
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            (string.IsNullOrWhiteSpace(accessToken) &&
             (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))))
        {
            return;
        }

        var manifestPath = Environment.GetEnvironmentVariable($"LIVE_{prefix}_CONTRACT_MANIFEST_PATH")
            ?? Environment.GetEnvironmentVariable("MOODLE_API_CONTRACT_MANIFEST_PATH")
            ?? Environment.GetEnvironmentVariable("MoodleApi__ContractManifestPath");
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new InvalidOperationException(
                $"LIVE_{prefix}_CONTRACT_MANIFEST_PATH or MOODLE_API_CONTRACT_MANIFEST_PATH is required when live credentials are configured.");
        }

        var connection = new ConnectionInfo(Guid.NewGuid(), alias, baseUrl);
        var credentials = new MoodleConnectorCredentials(
            "live-generic-contract-test",
            connection.ConnectionId.ToString(),
            alias,
            baseUrl,
            username ?? string.Empty,
            password ?? string.Empty,
            "moodle",
            false);
        var credentialsProvider = new FixedCredentialsProvider(credentials);
        var restClient = new MoodleRestClient(new HttpClient(), new LoginTokenProvider(accessToken),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MoodleRestClient>.Instance);

        var siteInfo = await restClient.CallAsync(
            credentials,
            "core_webservice_get_site_info",
            new Dictionary<string, object?>(),
            default);
        var siteInfoDocument = JsonDocument.Parse(siteInfo.GetRawText());
        var release = siteInfoDocument.RootElement.GetProperty("release").GetString();
        var functionVersion = siteInfoDocument.RootElement.GetProperty("functions")
            .EnumerateArray()
            .Where(function => function.TryGetProperty("name", out var name) &&
                               string.Equals(name.GetString(), "core_webservice_get_site_info", StringComparison.OrdinalIgnoreCase))
            .Select(function => function.TryGetProperty("version", out var version) ? version.GetString() : null)
            .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));
        Assert.False(string.IsNullOrWhiteSpace(release));
        Assert.False(string.IsNullOrWhiteSpace(functionVersion));

        var contracts = MoodleFunctionContractManifestLoader.Load(
            manifestPath,
            maxBytes: 50 * 1024 * 1024,
            requireVerifiedContracts: true);
        var contractRegistry = new MoodleFunctionContractRegistry(contracts);
        var resolution = contractRegistry.Resolve(
            "core_webservice_get_site_info",
            release,
            functionVersion);
        Assert.True(
            resolution.IsVerified,
            $"No verified live contract for core_webservice_get_site_info: {string.Join(", ", resolution.Reasons)}");
        Assert.Equal(MoodleEffect.Read, resolution.Contract!.Effect);
        var operationRegistry = new OperationRegistry(contractRegistry);
        var capabilityRegistry = new CapabilityRegistry(restClient, credentialsProvider);
        var safeRead = new SafeReadExecutor(
            new SingleConnectionRegistry(connection),
            operationRegistry,
            capabilityRegistry,
            new PolicyEngine(),
            new ResponseNormalizer(),
            credentialsProvider,
            restClient,
            contractRegistry,
            Options.Create(new MoodleFunctionContractOptions { RequireVerifiedContracts = true }),
            new LiveReadUser());

        var result = await safeRead.ExecuteAsync(
            "core_webservice_get_site_info",
            new Dictionary<string, object?>(),
            alias,
            new NormalizationContext(NormalizationMode.Agent),
            default);

        Assert.NotNull(result);
    }

    private sealed class SingleConnectionRegistry(ConnectionInfo connection) : IConnectionRegistry
    {
        public Task<ConnectionInfo?> ResolveConnectionAsync(string? alias, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConnectionInfo?>(
                string.IsNullOrWhiteSpace(alias) || string.Equals(alias, connection.Alias, StringComparison.OrdinalIgnoreCase)
                    ? connection
                    : null);
    }

    private sealed class FixedCredentialsProvider(MoodleConnectorCredentials credentials) : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(credentials);
    }

    private sealed class LiveReadUser : ICurrentUserContext
    {
        public string Subject => "live-generic-contract-test";

        public string? Email => null;

        public IReadOnlyCollection<string> Scopes => ["moodle.read"];

        public bool HasScope(string scope) =>
            string.Equals(scope, "moodle.read", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LoginTokenProvider(string? configuredToken) : IMoodleAccessTokenProvider
    {
        private readonly HttpClient httpClient = new();

        public async Task<string> GetAccessTokenAsync(
            MoodleConnectorCredentials credentials,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(configuredToken))
            {
                return configuredToken;
            }

            var query = string.Join("&", new Dictionary<string, string>
            {
                ["username"] = credentials.Username ?? string.Empty,
                ["password"] = credentials.Password ?? string.Empty,
                ["service"] = "moodle_mobile_app"
            }.Select(item =>
                $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
            var endpoint = $"{credentials.BaseUrl.TrimEnd('/')}/login/token.php?{query}";
            using var response = await httpClient.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("token", out var token) &&
                !string.IsNullOrWhiteSpace(token.GetString()))
            {
                return token.GetString()!;
            }

            throw new InvalidOperationException("Moodle did not return a token for the live shadow test.");
        }

        public void Invalidate(MoodleConnectorCredentials credentials)
        {
        }
    }
}
