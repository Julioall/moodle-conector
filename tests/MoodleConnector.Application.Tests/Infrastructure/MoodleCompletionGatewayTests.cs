using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleCompletionGatewayTests
{
    [Fact]
    public async Task Nao_converte_falha_da_funcao_de_conclusao_do_curso_em_nao_concluido()
    {
        var sut = CreateGateway(new FakeRestClient(functionName =>
        {
            if (functionName == "core_completion_get_course_completion_status")
            {
                throw new MoodleApiException(MoodleErrorContract.FunctionNotAllowed, "Funcao indisponivel.");
            }

            return Payload("""{"statuses":[]}""");
        }));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetStudentCompletionAsync("10", "20", CancellationToken.None));

        Assert.Equal(MoodleErrorContract.FunctionNotAllowed, error.ErrorCode);
    }

    [Fact]
    public async Task Nao_converte_payload_de_conclusao_incompleto_em_nao_concluido()
    {
        var sut = CreateGateway(new FakeRestClient(functionName => functionName switch
        {
            "core_completion_get_course_completion_status" => Payload("""{"completionstatus":{}}"""),
            _ => Payload("""{"statuses":[]}""")
        }));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetStudentCompletionAsync("10", "20", CancellationToken.None));

        Assert.Equal(MoodleErrorContract.InvalidResponse, error.ErrorCode);
        Assert.Equal("core_completion_get_course_completion_status", error.FunctionName);
        Assert.Equal(MoodleIntegrationStage.ResponseParsing, error.Stage);
    }

    [Fact]
    public async Task Nao_converte_payload_de_atividades_incompleto_em_lista_vazia()
    {
        var sut = CreateGateway(new FakeRestClient(functionName => functionName switch
        {
            "core_completion_get_course_completion_status" => Payload("""{"completionstatus":{"completed":false}}"""),
            _ => Payload("""{"unexpected":[]}""")
        }));

        var error = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.GetStudentCompletionAsync("10", "20", CancellationToken.None));

        Assert.Equal(MoodleErrorContract.InvalidResponse, error.ErrorCode);
        Assert.Equal("core_completion_get_activities_completion_status", error.FunctionName);
        Assert.Equal(MoodleIntegrationStage.ResponseParsing, error.Stage);
    }

    private static MoodleCompletionGateway CreateGateway(IMoodleRestClient restClient) =>
        new(
            Options.Create(new MoodleApiOptions()),
            new FakeCredentialsProvider(),
            restClient);

    private static JsonElement Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FakeRestClient(Func<string, JsonElement> responseFactory) : IMoodleRestClient
    {
        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(functionName));

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(functionName));
    }

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client",
                "connection",
                "default",
                "https://moodle.example",
                "user",
                "password",
                "target",
                false));
    }
}
