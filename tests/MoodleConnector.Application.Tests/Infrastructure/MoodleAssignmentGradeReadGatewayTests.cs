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

        [Fact]
        public async Task Preserva_grader_e_timemodified_para_atividade_sem_nota()
        {
                var sut = new MoodleAssignmentGradeReadGateway(
                        Options.Create(new MoodleApiOptions()),
                        new FakeCredentialsProvider(),
                        new FakeRestClient("""
                        {
                            "assignments": [{
                                "assignmentid": 117487,
                                "grades": [
                                    { "userid": 440752, "grade": "-1.00000", "grader": 317295, "timemodified": 1787075877 },
                                    { "userid": 440739, "grade": "-1.00000", "grader": -1, "timemodified": 0 }
                                ]
                            }]
                        }
                        """));

                var grades = await sut.GetExistingGradesAsync(
                        "teacher-1",
                        "117487",
                        ["440752", "440739"],
                        CancellationToken.None);

                Assert.Equal(317295, grades["440752"].GraderId);
                Assert.Equal(1787075877, grades["440752"].TimeModified);
                Assert.Equal(-1, grades["440739"].GraderId);
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
