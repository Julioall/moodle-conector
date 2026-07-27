using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure.MoodleApi;

public sealed class MoodleCoursesCapabilityGatewayTests
{
    [Theory]
    [InlineData("core_course_get_enrolled_courses_by_timeline_classification", "core_course_get_enrolled_courses_by_timeline_classification")]
    [InlineData("core_enrol_get_users_courses", "core_enrol_get_users_courses")]
    public async Task GetMyCoursesAsync_SelecionaEstrategiaDisponivelDaConexao(string availableFunction, string expectedFunction)
    {
        var restClient = new FakeRestClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile(availableFunction)),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var page = await gateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(expectedFunction, Assert.Single(restClient.Calls));
    }

    [Fact]
    public async Task GetMyCourseAsync_ConsultaCursoExatoEValidaMatriculaSemListarTodosOsCursos()
    {
        var restClient = new FakeRestClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile("core_course_get_courses_by_field", "core_enrol_get_enrolled_users")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var course = await gateway.GetMyCourseAsync("7", "42", CancellationToken.None);

        Assert.NotNull(course);
        Assert.Equal(["core_course_get_courses_by_field", "core_enrol_get_enrolled_users"], restClient.Calls);
    }

    [Fact]
    public async Task GetMyCourseAsync_RecusaCursoExatoSemMatricula()
    {
        var restClient = new FakeRestClient { EnrolledUserId = 8 };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile("core_course_get_courses_by_field", "core_enrol_get_enrolled_users")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var error = await Assert.ThrowsAsync<MoodleApiException>(() => gateway.GetMyCourseAsync("7", "42", CancellationToken.None));

        Assert.Equal("not_enrolled", error.ErrorCode);
        Assert.Equal(["core_course_get_courses_by_field", "core_enrol_get_enrolled_users"], restClient.Calls);
    }

    private static MoodleFunctionProfile Profile(params string[] functions) => new(
        "connection", "goias", "Moodle GoiÃ¡s", "4.5", 7,
        functions.Select(function => new MoodleFunctionDescriptor(function, MoodleFunctionRisk.Read, true)).ToArray(),
        DateTimeOffset.UtcNow);

    private sealed class FakeCatalog(MoodleFunctionProfile profile) : IMoodleFunctionCatalog
    {
        public Task<MoodleFunctionProfile> GetCurrentAsync(bool forceRefresh, CancellationToken cancellationToken) => Task.FromResult(profile);
    }

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials("client", "connection", "goias", "https://moodle.example", "user", "password", "goias", false));
    }

    private sealed class FakeCurrentUserIdGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) => Task.FromResult(7L);
    }

    private sealed class FakeRestClient : IMoodleRestClient
    {
        public List<string> Calls { get; } = [];
        public int EnrolledUserId { get; init; } = 7;

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, true, cancellationToken);

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, bool allowServiceToken, CancellationToken cancellationToken)
        {
            Calls.Add(functionName);
            var payload = functionName switch
            {
                "core_course_get_enrolled_courses_by_timeline_classification" => "{\"courses\":[{\"id\":42,\"fullname\":\"Curso ativo\"}]}",
                "core_course_get_courses_by_field" => "{\"courses\":[{\"id\":42,\"fullname\":\"Curso ativo\"}]}",
                "core_enrol_get_enrolled_users" => $"[{{\"id\":{EnrolledUserId}}}]",
                _ => "[{\"id\":42,\"fullname\":\"Curso ativo\"}]"
            };
            using var document = JsonDocument.Parse(payload);
            return Task.FromResult(document.RootElement.Clone());
        }
    }
}
