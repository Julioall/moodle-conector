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
            new FakeCatalog(Profile(availableFunction, "core_course_get_categories")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var page = await gateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(expectedFunction, restClient.Calls[0]);
        Assert.Equal("core_course_get_categories", restClient.Calls[^1]);
    }

    [Fact]
    public async Task GetMyCoursesAsync_PrefereCursosMatriculadosEComplementaCategoriasQuandoNecessario()
    {
        var restClient = new FakeRestClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile(
                "core_course_get_enrolled_courses_by_timeline_classification",
                "core_enrol_get_users_courses",
                "core_course_get_categories")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var page = await gateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(
            ["core_enrol_get_users_courses", "core_course_get_enrolled_courses_by_timeline_classification", "core_course_get_categories"],
            restClient.Calls);
    }

    [Fact]
    public async Task GetMyCoursesAsync_NaoChamaFallbackNaoAnunciadoPelaConexao()
    {
        var restClient = new FakeRestClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile(
                "core_course_get_enrolled_courses_by_timeline_classification",
                "core_course_get_categories")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var page = await gateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(["core_course_get_enrolled_courses_by_timeline_classification", "core_course_get_categories"], restClient.Calls);
    }

    [Fact]
    public async Task GetMyCoursesAsync_UsaCaminhoDaCategoriaQuandoMoodleRetornaCategoryNameResumido()
    {
        var restClient = new FakeRestClient { UseNestedCategory = true };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile("core_enrol_get_users_courses", "core_course_get_categories")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var page = await gateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None);

        Assert.Equal("CTM/ DR-GO > DR-MT", Assert.Single(page.Items).CategoryName);
    }

    [Fact]
    public async Task GetMyCoursesAsync_EnriqueceCategoriasPelaTimelineQuandoCursosMatriculadosNaoTrazemCategoryId()
    {
        var restClient = new FakeRestClient { UseNestedCategory = true, EnrolledMissingCategoryId = true };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile(
                "core_course_get_enrolled_courses_by_timeline_classification",
                "core_enrol_get_users_courses",
                "core_course_get_categories")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var page = await gateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None);

        Assert.Equal("CTM/ DR-GO > DR-MT", Assert.Single(page.Items).CategoryName);
        Assert.Equal(
            ["core_enrol_get_users_courses", "core_course_get_enrolled_courses_by_timeline_classification", "core_course_get_categories"],
            restClient.Calls);
    }

    [Fact]
    public async Task GetMyCoursesAsync_Coalesces_concurrent_cold_cache_reads()
    {
        var restClient = new FakeRestClient { ResponseDelay = TimeSpan.FromMilliseconds(25) };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile("core_enrol_get_users_courses", "core_course_get_categories")),
            new FakeCurrentUserIdGateway(),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var pages = await Task.WhenAll(
            Enumerable.Range(0, 3)
                .Select(_ => gateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None)));

        Assert.All(pages, page => Assert.Single(page.Items));
        Assert.Equal(1, restClient.CountCalls("core_enrol_get_users_courses"));
        Assert.Equal(1, restClient.CountCalls("core_course_get_categories"));
    }

    [Fact]
    public async Task GetMyCoursesAsync_NaoMantemRespostaVaziaDeCategoriasNoCache()
    {
        var restClient = new FakeRestClient { UseNestedCategory = true, EmptyCategoriesFirst = true };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var firstGateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile("core_enrol_get_users_courses", "core_course_get_categories")),
            new FakeCurrentUserIdGateway(7),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());
        var secondGateway = new MoodleCoursesGateway(
            Options.Create(new MoodleApiOptions()),
            cache,
            new FakeCredentialsProvider(),
            restClient,
            new FakeCatalog(Profile("core_enrol_get_users_courses", "core_course_get_categories")),
            new FakeCurrentUserIdGateway(8),
            new MoodleBusinessFlowRegistry(),
            new MoodleResourceResolver());

        var firstPage = await firstGateway.GetMyCoursesAsync("7", 20, 1, CancellationToken.None);
        var secondPage = await secondGateway.GetMyCoursesAsync("8", 20, 1, CancellationToken.None);

        Assert.Equal("CTM/ DR-GO", Assert.Single(firstPage.Items).CategoryName);
        Assert.Equal("CTM/ DR-GO > DR-MT", Assert.Single(secondPage.Items).CategoryName);
        Assert.Equal(2, restClient.CountCalls("core_course_get_categories"));
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
        "connection", "goias", "Moodle Goiás", "4.5", 7,
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

    private sealed class FakeCurrentUserIdGateway(long moodleUserId = 7) : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) => Task.FromResult(moodleUserId);
    }

    private sealed class FakeRestClient : IMoodleRestClient
    {
        public List<string> Calls { get; } = [];
        public int EnrolledUserId { get; init; } = 7;
        public bool UseNestedCategory { get; init; }
        public bool EmptyCategoriesFirst { get; init; }
        public bool EnrolledMissingCategoryId { get; init; }
        public TimeSpan ResponseDelay { get; init; }
        private int categoryCallCount;

        public int CountCalls(string functionName)
        {
            lock (Calls)
            {
                return Calls.Count(call => call == functionName);
            }
        }

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, true, cancellationToken);

        public async Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, bool allowServiceToken, CancellationToken cancellationToken)
        {
            if (ResponseDelay > TimeSpan.Zero)
            {
                await Task.Delay(ResponseDelay, cancellationToken);
            }

            lock (Calls)
            {
                Calls.Add(functionName);
            }
            var payload = functionName switch
            {
                "core_course_get_enrolled_courses_by_timeline_classification" => UseNestedCategory
                    ? "{\"courses\":[{\"id\":42,\"fullname\":\"Curso ativo\",\"categoryid\":2,\"categoryname\":\"CTM/ DR-GO\"}]}"
                    : "{\"courses\":[{\"id\":42,\"fullname\":\"Curso ativo\"}]}",
                "core_enrol_get_users_courses" => UseNestedCategory
                    ? EnrolledMissingCategoryId
                        ? "[{\"id\":42,\"fullname\":\"Curso ativo\",\"categoryname\":\"CTM/ DR-GO\"}]"
                        : "[{\"id\":42,\"fullname\":\"Curso ativo\",\"categoryid\":2,\"categoryname\":\"CTM/ DR-GO\"}]"
                    : "[{\"id\":42,\"fullname\":\"Curso ativo\"}]",
                "core_course_get_courses_by_field" => "{\"courses\":[{\"id\":42,\"fullname\":\"Curso ativo\"}]}",
                "core_enrol_get_enrolled_users" => $"[{{\"id\":{EnrolledUserId}}}]",
                "core_course_get_categories" => EmptyCategoriesFirst && Interlocked.Increment(ref categoryCallCount) == 1
                    ? "[]"
                    : UseNestedCategory
                    ? "[{\"id\":1,\"name\":\"CTM/ DR-GO\",\"parent\":0},{\"id\":2,\"name\":\"DR-MT\",\"parent\":1,\"path\":\"/1/2\"}]"
                    : "[{\"id\":42,\"name\":\"Curso\",\"parent\":0}]",
                _ => "[{\"id\":42,\"fullname\":\"Curso ativo\"}]"
            };
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
    }
}
