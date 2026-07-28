using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Courses;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Application.Tests.Tools.Courses;

public class MoodleCoursesToolsTests
{
    [Fact]
    public async Task Deve_usar_usuario_moodle_resolvido_na_consulta_de_cursos()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var resolver = new FakeMoodleUserResolver(777);
        var sut = new MoodleCoursesTools(mediator, selection, resolver);

        var result = await sut.ListarMeusCursosAsync(3, 1, "goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);
        Assert.NotNull(mediator.LastListQuery);
        Assert.Equal("777", mediator.LastListQuery!.UserExternalId);
        Assert.Equal(3, mediator.LastListQuery.Limit);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.True(structured.TryGetProperty("data", out var data));
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        var course = data.GetProperty("courses")[0];
        Assert.Equal("1", course.GetProperty("courseId").GetString());
        Assert.Equal("ID-1", course.GetProperty("idNumber").GetString());
        Assert.Equal("CURSO", course.GetProperty("shortName").GetString());
        Assert.Equal("Curso", course.GetProperty("fullName").GetString());
        Assert.Equal("Curso Display", course.GetProperty("displayName").GetString());
        Assert.Equal(10, course.GetProperty("categoryId").GetInt64());
        Assert.Equal("Categoria", course.GetProperty("categoryName").GetString());
        Assert.True(course.GetProperty("visible").GetBoolean());
        Assert.Equal("https://moodle.example/course/view.php?id=1", course.GetProperty("viewUrl").GetString());
        Assert.Equal("https://moodle.example/pluginfile.php/course.png", course.GetProperty("courseImage").GetString());
        Assert.Equal(75.5m, course.GetProperty("progress").GetDecimal());
        Assert.True(course.GetProperty("hasProgress").GetBoolean());
        Assert.False(course.GetProperty("isFavourite").GetBoolean());
        Assert.False(course.TryGetProperty("currentGrade", out _));
        Assert.False(course.TryGetProperty("nextDueAt", out _));
        Assert.False(course.TryGetProperty("pendingActivities", out _));
        Assert.True(structured.TryGetProperty("warnings", out _));
        Assert.True(structured.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task Deve_falhar_quando_usuario_moodle_nao_for_resolvido()
    {
        var sut = new MoodleCoursesTools(
            new FakeMediator(),
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(null));

        var result = await sut.ListarMeusCursosAsync(cancellationToken: CancellationToken.None);

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("error", structured.GetProperty("status").GetString());
        Assert.Equal("Usuario nao autenticado para listar cursos.", structured.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task Deve_retornar_lista_vazia_quando_usuario_nao_possuir_cursos()
    {
        var mediator = new FakeMediator { ReturnEmpty = true };
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarMeusCursosAsync(cancellationToken: CancellationToken.None);

        Assert.False(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal(0, data.GetProperty("total").GetInt32());
        Assert.Equal(0, data.GetProperty("courses").GetArrayLength());
    }

    [Fact]
    public async Task Deve_retornar_erro_controlado_quando_consulta_moodle_falhar()
    {
        var mediator = new FakeMediator { ThrowOnList = true };
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarMeusCursosAsync(cancellationToken: CancellationToken.None);

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("error", structured.GetProperty("status").GetString());
        Assert.Equal("Nao foi possivel listar os cursos no Moodle neste momento.", structured.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task Deve_buscar_cursos_por_termo()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var sut = new MoodleCoursesTools(mediator, selection, new FakeMoodleUserResolver(777));

        var result = await sut.BuscarCursosAsync("seguranca", 4, "goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);
        Assert.Equal("seguranca", mediator.LastSearchQuery?.Query);
        Assert.Equal(4, mediator.LastSearchQuery?.Limit);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(1, structured.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Deve_consultar_curso_por_identificador()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ConsultarCursoAsync("CURSO");

        Assert.False(result.IsError ?? false);
        Assert.Equal("CURSO", mediator.LastGetCourseQuery?.CourseId);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.Equal("1", structured.GetProperty("data").GetProperty("course").GetProperty("courseId").GetString());
    }

    [Fact]
    public async Task Deve_expor_search_no_formato_padrao_de_connector()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.SearchAsync("seguranca");

        Assert.False(result.IsError ?? false);
        Assert.Equal("seguranca", mediator.LastSearchQuery?.Query);
        Assert.Equal(10, mediator.LastSearchQuery?.Limit);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var searchResult = Assert.Single(structured.GetProperty("results").EnumerateArray());
        Assert.Equal("1", searchResult.GetProperty("id").GetString());
        Assert.Equal("Curso Display", searchResult.GetProperty("title").GetString());
        Assert.Equal("https://moodle.example/course/view.php?id=1", searchResult.GetProperty("url").GetString());

        var content = Assert.Single(result.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(content).Text;
        using var contentJson = JsonDocument.Parse(text);
        Assert.True(contentJson.RootElement.TryGetProperty("results", out _));
    }

    [Fact]
    public async Task Deve_expor_fetch_no_formato_padrao_de_connector()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.FetchAsync("CURSO");

        Assert.False(result.IsError ?? false);
        Assert.Equal("CURSO", mediator.LastGetCourseQuery?.CourseId);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("1", structured.GetProperty("id").GetString());
        Assert.Equal("Curso Display", structured.GetProperty("title").GetString());
        Assert.Contains("Curso: Curso", structured.GetProperty("text").GetString());
        Assert.Equal("https://moodle.example/course/view.php?id=1", structured.GetProperty("url").GetString());
        Assert.Equal("CURSO", structured.GetProperty("metadata").GetProperty("shortName").GetString());

        var content = Assert.Single(result.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(content).Text;
        using var contentJson = JsonDocument.Parse(text);
        Assert.Equal("1", contentJson.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Deve_retornar_erro_controlado_quando_curso_nao_for_encontrado()
    {
        var mediator = new FakeMediator { ReturnNullCourse = true };
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ConsultarCursoAsync("inexistente");

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("error", structured.GetProperty("status").GetString());
        Assert.Equal("Curso nao encontrado entre os cursos vinculados ao usuario.", structured.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task Deve_incluir_metadados_de_paginacao_na_resposta()
    {
        var mediator = new FakeMediator { SimulatedTotal = 30, SimulatedPageSize = 10 };
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarMeusCursosAsync(10, 2);

        Assert.False(result.IsError ?? false);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.Equal(30, data.GetProperty("total").GetInt32());
        Assert.Equal(2, data.GetProperty("page").GetInt32());
        Assert.Equal(3, data.GetProperty("total_pages").GetInt32());
        Assert.True(data.GetProperty("has_next_page").GetBoolean());
    }

    [Fact]
    public async Task Deve_repassar_pagina_corretamente_para_query()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        await sut.ListarMeusCursosAsync(10, 4);

        Assert.Equal(4, mediator.LastListQuery?.Page);
        Assert.Equal(10, mediator.LastListQuery?.Limit);
    }

    [Fact]
    public async Task Deve_incluir_hint_de_proxima_pagina_na_narracao()
    {
        var mediator = new FakeMediator { SimulatedTotal = 30, SimulatedPageSize = 10 };
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarMeusCursosAsync(10, 1);

        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("Pagina 1 de 3", text);
        Assert.Contains("pagina=2", text);
    }

    [Fact]
    public async Task Nao_deve_incluir_hint_na_narracao_quando_nao_houver_proxima_pagina()
    {
        var mediator = new FakeMediator { SimulatedTotal = 5, SimulatedPageSize = 10 };
        var sut = new MoodleCoursesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarMeusCursosAsync(10, 1);

        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain("Pagina", text);
        Assert.DoesNotContain("pagina=", text);
    }

    [Fact]
    public async Task ListarMeusCursosAsync_NuncaDeixaFalhaDeResolucaoEscaparAoMcp()
    {
        var sut = new MoodleCoursesTools(
            new FakeMediator(),
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(
                error: new MoodleApiException(
                    MoodleErrorContract.ConnectionNotFound,
                    "internal")));

        var result = await sut.ListarMeusCursosAsync(moodleAlias: "goias");

        Assert.True(result.IsError);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(MoodleErrorContract.ConnectionNotFound, structured.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(structured.GetProperty("auditId").GetString()));
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("data").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(structured.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task BuscarCursosAsync_DevolveConexaoDesativadaComContratoEstavel()
    {
        var sut = new MoodleCoursesTools(
            new FakeMediator(),
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(
                error: new MoodleApiException(
                    MoodleErrorContract.ConnectionDisabled,
                    "internal")));

        var result = await sut.BuscarCursosAsync("32786", moodleAlias: "goias");

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(MoodleErrorContract.ConnectionDisabled, structured.GetProperty("errorCode").GetString());
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ConsultarCursoAsync_ConverteExcecaoInesperadaSemExporTipo()
    {
        var sut = new MoodleCoursesTools(
            new FakeMediator(),
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(error: new InvalidOperationException("secret detail")));

        var result = await sut.ConsultarCursoAsync("32786", "goias");

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(MoodleErrorContract.Unexpected, structured.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("InvalidOperationException", structured.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret detail", structured.GetRawText(), StringComparison.Ordinal);
    }

    private sealed class FakeMoodleConnectionSelection : IMoodleConnectionSelection
    {
        public string? Alias { get; set; }
    }

    private sealed class FakeMoodleUserResolver(
        long? userId = null,
        Exception? error = null) : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken)
        {
            if (error is not null)
            {
                throw error;
            }

            return Task.FromResult(userId);
        }
    }

    private sealed class FakeMediator : IMediator
    {
        public ListMyCoursesQuery? LastListQuery { get; private set; }

        public SearchCoursesQuery? LastSearchQuery { get; private set; }

        public GetCourseQuery? LastGetCourseQuery { get; private set; }

        public bool ReturnEmpty { get; init; }

        public bool ThrowOnList { get; init; }

        public bool ReturnNullCourse { get; init; }

        public int SimulatedTotal { get; init; } = 1;

        public int SimulatedPageSize { get; init; } = 20;

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ListMyCoursesQuery list)
            {
                LastListQuery = list;
                if (ThrowOnList)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                var course = CreateCourseSummary();
                IReadOnlyList<CourseSummary> courses = ReturnEmpty ? [] : [course];
                var effectiveTotal = ReturnEmpty ? 0 : SimulatedTotal;
                var paged = new PagedCourses(courses, effectiveTotal, list.Page, SimulatedPageSize);
                return Task.FromResult((TResponse)(object)paged);
            }

            if (request is SearchCoursesQuery search)
            {
                LastSearchQuery = search;
                IReadOnlyList<CourseSummary> data = [CreateCourseSummary()];
                return Task.FromResult((TResponse)data);
            }

            if (request is GetCourseQuery getCourse)
            {
                LastGetCourseQuery = getCourse;
                return ReturnNullCourse
                    ? Task.FromResult<TResponse>(default!)
                    : Task.FromResult((TResponse)(object)CreateCourseSummary());
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is ListMyCoursesQuery list)
            {
                LastListQuery = list;
                if (ThrowOnList)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                var course = CreateCourseSummary();
                IReadOnlyList<CourseSummary> courses = ReturnEmpty ? [] : [course];
                var effectiveTotal = ReturnEmpty ? 0 : SimulatedTotal;
                var paged = new PagedCourses(courses, effectiveTotal, list.Page, SimulatedPageSize);
                return Task.FromResult<object?>(paged);
            }

            if (request is SearchCoursesQuery search)
            {
                LastSearchQuery = search;
                IReadOnlyList<CourseSummary> data = [CreateCourseSummary()];
                return Task.FromResult<object?>(data);
            }

            if (request is GetCourseQuery getCourse)
            {
                LastGetCourseQuery = getCourse;
                return Task.FromResult<object?>(ReturnNullCourse ? null : CreateCourseSummary());
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();

        private static CourseSummary CreateCourseSummary()
        {
            return new CourseSummary(
                "1",
                "ID-1",
                "CURSO",
                "Curso",
                "Curso Display",
                10,
                "Categoria",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
                true,
                "https://moodle.example/course/view.php?id=1",
                "https://moodle.example/pluginfile.php/course.png",
                75.5m,
                true,
                false,
                new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        }
    }
}
