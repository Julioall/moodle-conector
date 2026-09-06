using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;
using System.Text.Json;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleAssignmentSettingsGatewayTests
{
    [Fact]
    public async Task StubSemEscalaNaoFabricaMaximoNumerico()
    {
        var gateway = new MoodleAssignmentSettingsGateway(
            Options.Create(new MoodleApiOptions { UseStubData = true }),
            credentialsProvider: null!,
            restClient: null!,
            new MemoryCache(new MemoryCacheOptions()));

        var result = await gateway.GetAssignmentSettingsAsync(
            "teacher",
            "101",
            "5001",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.MaxGrade);
    }

    [Fact]
    public async Task CacheDeConfiguracaoIsolaConexaoEUsuario()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var credentials = new SwitchingCredentialsProvider();
        var restClient = new ConnectionAwareRestClient();
        var gateway = new MoodleAssignmentSettingsGateway(
            Options.Create(new MoodleApiOptions { UseStubData = false }),
            credentials,
            restClient,
            cache);

        credentials.Current = Credentials("client-a", "connection-a", "teacher-a");
        var first = await gateway.GetAssignmentSettingsAsync("teacher-a", "101", "5001", CancellationToken.None);

        credentials.Current = Credentials("client-b", "connection-b", "teacher-b");
        var second = await gateway.GetAssignmentSettingsAsync("teacher-b", "101", "5001", CancellationToken.None);

        Assert.Equal(10m, first!.MaxGrade);
        Assert.Equal(20m, second!.MaxGrade);
        Assert.Equal(2, restClient.CallCount);
    }

    private static MoodleConnectorCredentials Credentials(string clientId, string connectionId, string username) =>
        new(clientId, connectionId, connectionId, "https://moodle.example", username, "password", "moodle", false);

    private sealed class SwitchingCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public MoodleConnectorCredentials Current { get; set; } = Credentials("client-a", "connection-a", "teacher-a");

        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current);
    }

    private sealed class ConnectionAwareRestClient : IMoodleRestClient
    {
        public int CallCount { get; private set; }

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, allowServiceToken: false, cancellationToken);

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var grade = connection.ConnectionId == "connection-a" ? 10 : 20;
            using var document = JsonDocument.Parse($"{{\"courses\":[{{\"assignments\":[{{\"id\":5001,\"grade\":{grade}}}]}}]}}");
            return Task.FromResult(document.RootElement.Clone());
        }
    }
}
