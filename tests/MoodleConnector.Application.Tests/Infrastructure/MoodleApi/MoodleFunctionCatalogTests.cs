using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure.MoodleApi;

public sealed class MoodleFunctionCatalogTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"functions\":null}")]
    [InlineData("{\"functions\":{}}")]
    [InlineData("{\"functions\":[{}]}")]
    [InlineData("{\"functions\":[{\"name\":12}]}")]
    [InlineData("{\"functions\":[{\"name\":\" \"}]}")]
    public async Task Invalid_function_profiles_are_not_cached_as_empty_capabilities(string invalidPayload)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var rest = new RecoveringRestClient(invalidPayload);
        var catalog = new MoodleFunctionCatalog(cache, rest, new SwitchingCredentialsProvider());

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            catalog.GetCurrentAsync(false, CancellationToken.None));
        Assert.Equal(MoodleErrorContract.InvalidResponse, error.ErrorCode);
        Assert.Equal(MoodleIntegrationStage.ResponseParsing, error.Stage);

        var recovered = await catalog.GetCurrentAsync(false, CancellationToken.None);
        Assert.Single(recovered.Functions);
        Assert.False(recovered.IsCached);
        Assert.True((await catalog.GetCurrentAsync(false, CancellationToken.None)).IsCached);
        Assert.Equal(2, rest.Calls);
    }

    [Fact]
    public void Explicit_empty_functions_is_a_valid_profile()
    {
        using var payload = JsonDocument.Parse("{\"functions\":[]}");
        var connection = new MoodleConnectorCredentials("client", "id", "alias", "https://moodle.example",
            "user", "password", "alias", false);
        Assert.Empty(MoodleFunctionProfileParser.Parse(connection, payload.RootElement).Functions);
    }

    private sealed class RecoveringRestClient(string firstPayload) : IMoodleRestClient
    {
        public int Calls { get; private set; }
        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName,
            IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, false, cancellationToken);

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName,
            IReadOnlyDictionary<string, object?> parameters, bool allowServiceToken, CancellationToken cancellationToken)
        {
            Calls++;
            using var payload = JsonDocument.Parse(Calls == 1 ? firstPayload :
                "{\"functions\":[{\"name\":\"core_course_get_contents\"}]}");
            return Task.FromResult(payload.RootElement.Clone());
        }
    }

    [Fact]
    public async Task GetCurrentAsync_MantemPerfisIndependentesPorConexao()
    {
        var credentials = new SwitchingCredentialsProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var catalog = new MoodleFunctionCatalog(cache, new ProfileRestClient(), credentials);

        credentials.Current = "goias";
        var goias = await catalog.GetCurrentAsync(false, CancellationToken.None);
        credentials.Current = "senai";
        var senai = await catalog.GetCurrentAsync(false, CancellationToken.None);

        Assert.Equal("4.5", goias.Release);
        Assert.Contains(goias.Functions, function => function.Name == "core_enrol_get_users_courses");
        Assert.DoesNotContain(goias.Functions, function => function.Name == "core_course_get_enrolled_courses_by_timeline_classification");
        Assert.Equal("5.1.2", senai.Release);
        Assert.Contains(senai.Functions, function => function.Name == "core_course_get_enrolled_courses_by_timeline_classification");
    }

    [Fact]
    public async Task GetCurrentAsync_RefazDescobertaQuandoCredencialDaMesmaConexaoMuda()
    {
        var credentials = new SwitchingCredentialsProvider();
        var restClient = new ProfileRestClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var catalog = new MoodleFunctionCatalog(cache, restClient, credentials);

        await catalog.GetCurrentAsync(false, CancellationToken.None);
        credentials.Password = "token-rotacionado";
        await catalog.GetCurrentAsync(false, CancellationToken.None);

        Assert.Equal(2, restClient.Calls);
    }

    private sealed class SwitchingCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public string Current { get; set; } = "goias";
        public string Password { get; set; } = "password";

        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", Current, Current, "https://moodle.example", "user", Password, Current, false));
    }

    private sealed class ProfileRestClient : IMoodleRestClient
    {
        public int Calls { get; private set; }
        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, true, cancellationToken);

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, bool allowServiceToken, CancellationToken cancellationToken)
        {
            Calls++;
            var payload = connection.ConnectionId == "goias"
                ? "{\"sitename\":\"Goiás\",\"release\":\"4.5\",\"userid\":7,\"functions\":[{\"name\":\"core_enrol_get_users_courses\"}]}"
                : "{\"sitename\":\"SENAI\",\"release\":\"5.1.2\",\"userid\":8,\"functions\":[{\"name\":\"core_course_get_enrolled_courses_by_timeline_classification\"}]}";
            using var document = JsonDocument.Parse(payload);
            return Task.FromResult(document.RootElement.Clone());
        }
    }
}
