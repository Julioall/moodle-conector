using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleAssignmentGradeReadGatewayTests
{
    [Fact]
    public async Task Mantem_nota_vazia_distinta_do_marcador_menos_um_do_Moodle()
    {
        var sut = new MoodleAssignmentGradeReadGateway(
            Options.Create(new MoodleApiOptions()),
            new FakeCredentialsProvider(),
            new FakeRestClient("""
            {
              "assignments": [{
                "assignmentid": 116124,
                "grades": [
                  { "userid": 440752, "grade": "" },
                  { "userid": 440730, "grade": "-1.00000" }
                ]
              }]
            }
            """));

        var grades = await sut.GetExistingGradesAsync(
            "teacher-1",
            "116124",
            ["440752", "440730"],
            CancellationToken.None);

        Assert.Null(grades["440752"].Grade);
        Assert.Equal(-1m, grades["440730"].Grade);
    }

    private sealed class FakeRestClient(string json) : IMoodleRestClient
    {
        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) => Task.FromResult(Payload());

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken) => Task.FromResult(Payload());

        private JsonElement Payload()
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", "connection", "default", "https://moodle.example", "user", "password", "target", false));
    }
}
