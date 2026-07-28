using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Application.Tests.Tools;

public sealed class MoodleUniversalToolsTests
{
    [Fact]
    public async Task DiagnoseConnectionAsync_ExecutaSiteInfoAoVivoSemServiceToken()
    {
        var rest = new FakeRestClient(SiteInfo());
        var sut = CreateSut(new FakeCredentialsProvider(Connection()), rest);

        var result = await sut.DiagnoseConnectionAsync("Goiás", forceRefresh: true);

        Assert.False(result.IsError);
        Assert.False(rest.LastAllowServiceToken);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(structured.GetProperty("auditId").GetString()));
        var data = structured.GetProperty("data");
        Assert.True(data.GetProperty("healthy").GetBoolean());
        Assert.Equal("goias", data.GetProperty("requestedAlias").GetString());
        Assert.Equal("https://ead.fieg.com.br", data.GetProperty("baseUrl").GetString());
        Assert.True(data.GetProperty("siteInfoSucceeded").GetBoolean());
        Assert.Equal("5.0.1", data.GetProperty("release").GetString());
        Assert.False(data.TryGetProperty("token", out _));
        Assert.DoesNotContain("password", structured.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnoseConnectionAsync_RelataCheckpointDeDescriptografiaSemLancar()
    {
        var failure = new MoodleApiException(
            MoodleErrorContract.TokenDecryptionFailed,
            "internal",
            connectionId: "goias-connection",
            connectionAlias: "goias",
            endpoint: "https://ead.fieg.com.br",
            stage: MoodleIntegrationStage.CredentialDecryption);
        var sut = CreateSut(new FakeCredentialsProvider(error: failure), new FakeRestClient(SiteInfo()));

        var result = await sut.DiagnoseConnectionAsync("goias");

        Assert.False(result.IsError);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.False(data.GetProperty("healthy").GetBoolean());
        Assert.True(data.GetProperty("connectionFound").GetBoolean());
        Assert.True(data.GetProperty("active").GetBoolean());
        Assert.True(data.GetProperty("urlValid").GetBoolean());
        Assert.True(data.GetProperty("credentialsPresent").GetBoolean());
        Assert.False(data.GetProperty("decryptionSucceeded").GetBoolean());
        Assert.False(data.GetProperty("tokenAvailable").GetBoolean());
        Assert.False(data.GetProperty("authenticationSucceeded").GetBoolean());
        Assert.Equal(
            MoodleErrorContract.TokenDecryptionFailed,
            data.GetProperty("diagnosticErrorCode").GetString());
    }

    [Fact]
    public async Task DiagnoseConnectionAsync_NaoMarcaEtapasPosterioresQuandoUrlForInvalida()
    {
        var failure = new MoodleApiException(
            MoodleErrorContract.NetworkError,
            "internal",
            connectionId: "goias-connection",
            connectionAlias: "goias",
            stage: MoodleIntegrationStage.UrlValidation);
        var sut = CreateSut(new FakeCredentialsProvider(error: failure), new FakeRestClient(SiteInfo()));

        var result = await sut.DiagnoseConnectionAsync("goias");

        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.True(data.GetProperty("connectionFound").GetBoolean());
        Assert.True(data.GetProperty("active").GetBoolean());
        Assert.False(data.GetProperty("urlValid").GetBoolean());
        Assert.False(data.GetProperty("credentialsPresent").GetBoolean());
        Assert.False(data.GetProperty("decryptionSucceeded").GetBoolean());
        Assert.False(data.GetProperty("tokenAvailable").GetBoolean());
        Assert.False(data.GetProperty("authenticationSucceeded").GetBoolean());
    }

    private static MoodleUniversalTools CreateSut(
        IMoodleConnectorCredentialsProvider credentialsProvider,
        FakeRestClient restClient)
    {
        return new MoodleUniversalTools(
            new FakeCatalog(),
            new FakeExecutor(),
            new MoodleBusinessFlowRegistry(),
            credentialsProvider,
            new MoodleConnectionSelection(),
            restClient,
            NullLogger<MoodleUniversalTools>.Instance);
    }

    private static MoodleConnectorCredentials Connection() => new(
        "client",
        "goias-connection",
        "goias",
        "https://ead.fieg.com.br/?sensitive=query",
        "user",
        "password",
        "goias",
        false);

    private static JsonElement SiteInfo()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "sitename": "Moodle Goias",
              "release": "5.0.1",
              "userid": 847,
              "functions": [
                { "name": "core_webservice_get_site_info" },
                { "name": "core_course_get_courses_by_field" }
              ]
            }
            """);
        return document.RootElement.Clone();
    }

    private sealed class FakeCredentialsProvider(
        MoodleConnectorCredentials? connection = null,
        Exception? error = null) : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            error is null
                ? Task.FromResult(connection!)
                : Task.FromException<MoodleConnectorCredentials>(error);
    }

    private sealed class FakeRestClient(JsonElement payload) : IMoodleRestClient
    {
        public bool LastAllowServiceToken { get; private set; } = true;

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) => Task.FromResult(payload);

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken)
        {
            LastAllowServiceToken = allowServiceToken;
            return Task.FromResult(payload);
        }
    }

    private sealed class FakeCatalog : IMoodleFunctionCatalog
    {
        public Task<MoodleFunctionProfile> GetCurrentAsync(
            bool forceRefresh,
            CancellationToken cancellationToken) => Task.FromResult(new MoodleFunctionProfile(
            "goias-connection",
            "goias",
            "Moodle Goias",
            "5.0.1",
            847,
            [
                new MoodleFunctionDescriptor(
                    "core_webservice_get_site_info",
                    MoodleFunctionRisk.Read,
                    true),
                new MoodleFunctionDescriptor(
                    "core_course_get_courses_by_field",
                    MoodleFunctionRisk.Read,
                    true)
            ],
            DateTimeOffset.UtcNow));
    }

    private sealed class FakeExecutor : IMoodleFunctionExecutor
    {
        public Task<MoodleFunctionResult> ExecuteReadAsync(
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
