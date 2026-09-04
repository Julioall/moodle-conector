using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleAssignmentSubmissionsGatewayTests
{
    [Fact]
    public async Task Repete_leitura_de_submissoes_quando_falha_de_rede_e_transitoria()
    {
        var restClient = new FlakyRestClient();
        var sut = new MoodleAssignmentSubmissionsGateway(
            Options.Create(new MoodleApiOptions()),
            new FakeCredentialsProvider(),
            restClient);

        var result = await sut.GetAssignmentSubmissionsAsync(
            "teacher-1",
            "123",
            status: null,
            since: null,
            before: null,
            CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(2, restClient.Calls);
    }

    private sealed class FlakyRestClient : IMoodleRestClient
    {
        public int Calls { get; private set; }

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                throw new HttpRequestException("Falha transitoria simulada.");
            }

            using var document = JsonDocument.Parse(
                "{\"assignments\":[{\"assignmentid\":123,\"submissions\":[]}]}");
            return Task.FromResult(document.RootElement.Clone());
        }

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, cancellationToken);

        public Task<JsonElement> CallWriteAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, cancellationToken);
    }

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", "connection", "default", "https://moodle.example",
                "user", "password", "target", false));
    }
}
