using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleMessageGatewayTests
{
    [Fact]
    public async Task SendMessagesToUsersAsync_EnviaClientMessageIdDeterministicoEUsaCallWriteAsync()
    {
        var rest = new FakeRestClient("[{\"clientmsgid\":\"0\",\"msgid\":\"901\"},{\"clientmsgid\":\"1\",\"msgid\":902}]");
        var sut = CreateGateway(rest);

        var result = await sut.SendMessagesToUsersAsync("7", ["42", "43"], "Olá", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.SentCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, rest.WriteCalls);
        Assert.Equal(0, rest.ReadCalls);
        Assert.Equal("0", rest.LastWriteParameters!["messages[0][clientmsgid]"]);
        Assert.Equal("1", rest.LastWriteParameters["messages[1][clientmsgid]"]);
        Assert.Equal("42", rest.LastWriteParameters["messages[0][touserid]"]);
        Assert.Equal("43", rest.LastWriteParameters["messages[1][touserid]"]);
    }

    [Fact]
    public async Task SendMessagesToUsersAsync_CorrelacionaFalhaComDestinatarioMesmoForaDeOrdem()
    {
        var rest = new FakeRestClient("[{\"clientmsgid\":\"1\",\"msgid\":-1},{\"clientmsgid\":\"0\",\"msgid\":\"901\"}]");
        var sut = CreateGateway(rest);

        var result = await sut.SendMessagesToUsersAsync("7", ["42", "43"], "Olá", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(["43"], result.FailedUserIds);
    }

    [Fact]
    public async Task SendMessagesToUsersAsync_CorrelacionaMultiplasFalhas()
    {
        var rest = new FakeRestClient("[{\"clientmsgid\":\"2\",\"msgid\":-1},{\"clientmsgid\":\"0\",\"msgid\":-1},{\"clientmsgid\":\"1\",\"msgid\":903}]");
        var sut = CreateGateway(rest);

        var result = await sut.SendMessagesToUsersAsync("7", ["42", "43", "44"], "Olá", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(["42", "44"], result.FailedUserIds);
    }

    [Theory]
    [InlineData("[{\"clientmsgid\":\"invalid\",\"msgid\":-1}]")]
    [InlineData("[{\"clientmsgid\":\"0\",\"msgid\":901}]")]
    [InlineData("[{\"clientmsgid\":\"0\",\"msgid\":901},{\"clientmsgid\":\"0\",\"msgid\":902}]")]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("[]")]
    public async Task SendMessagesToUsersAsync_RespostaInesperadaFalhaFechada(string response)
    {
        var rest = new FakeRestClient(response);
        var sut = CreateGateway(rest);

        var result = await sut.SendMessagesToUsersAsync("7", ["42", "43"], "Olá", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.SentCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(["42", "43"], result.FailedUserIds);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    public async Task SendMessagesToUsersAsync_RespostaNulaOuVaziaMantemCompatibilidade(string response)
    {
        var rest = new FakeRestClient(response);
        var sut = CreateGateway(rest);

        var result = await sut.SendMessagesToUsersAsync("7", ["42", "43"], "Olá", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.SentCount);
        Assert.Empty(result.FailedUserIds);
    }

    [Fact]
    public async Task SendMessagesToUsersAsync_ErroDeLoteRetornaTodosOsDestinatarios()
    {
        var rest = new FakeRestClient("{\"exception\":\"moodle_exception\",\"errorcode\":\"nopermission\"}");
        var sut = CreateGateway(rest);

        var result = await sut.SendMessagesToUsersAsync("7", ["42", "43"], "Olá", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.SentCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(["42", "43"], result.FailedUserIds);
        Assert.Equal("nopermission", result.ErrorMessage);
    }

    private static MoodleMessageGateway CreateGateway(FakeRestClient rest) => new(
        Options.Create(new MoodleApiOptions()),
        new FakeCredentialsProvider(),
        rest,
        new FakeCurrentUserIdGateway());

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", "connection", "alias", "https://moodle.example", "user", "password", "alias", true));
    }

    private sealed class FakeCurrentUserIdGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) => Task.FromResult(7L);
    }

    private sealed class FakeRestClient(string response) : IMoodleRestClient
    {
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastWriteParameters { get; private set; }

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            return Task.FromResult(Parse(response));
        }

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            return Task.FromResult(Parse(response));
        }

        public Task<JsonElement> CallWriteAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken)
        {
            WriteCalls++;
            LastWriteParameters = new Dictionary<string, object?>(parameters);
            return Task.FromResult(Parse(response));
        }

        private static JsonElement Parse(string raw)
        {
            using var document = JsonDocument.Parse(string.IsNullOrEmpty(raw) ? "null" : raw);
            return document.RootElement.Clone();
        }
    }
}
