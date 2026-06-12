using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Courses;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Courses;

public class ListMyCoursesQueryHandlerTests
{
    [Fact]
    public async Task Deve_aplicar_clamp_de_limite_entre_1_e_20()
    {
        var gateway = new FakeGateway();
        var sut = new ListMyCoursesQueryHandler(gateway);

        await sut.Handle(new ListMyCoursesQuery("me", 999), CancellationToken.None);

        Assert.Equal(20, gateway.LastLimit);
    }

    [Fact]
    public async Task Deve_repassar_user_external_id_para_gateway()
    {
        var gateway = new FakeGateway();
        var sut = new ListMyCoursesQueryHandler(gateway);

        await sut.Handle(new ListMyCoursesQuery("usuario-42", 3), CancellationToken.None);

        Assert.Equal("usuario-42", gateway.LastUserExternalId);
    }

    [Fact]
    public async Task Deve_aplicar_clamp_na_busca_de_cursos()
    {
        var gateway = new FakeGateway();
        var sut = new SearchCoursesQueryHandler(gateway);

        await sut.Handle(new SearchCoursesQuery("usuario-42", "seguranca", 999), CancellationToken.None);

        Assert.Equal("usuario-42", gateway.LastUserExternalId);
        Assert.Equal("seguranca", gateway.LastQuery);
        Assert.Equal(20, gateway.LastLimit);
    }

    [Fact]
    public async Task Deve_consultar_curso_por_identificador()
    {
        var gateway = new FakeGateway();
        var sut = new GetCourseQueryHandler(gateway);

        await sut.Handle(new GetCourseQuery("usuario-42", "CURSO-1"), CancellationToken.None);

        Assert.Equal("usuario-42", gateway.LastUserExternalId);
        Assert.Equal("CURSO-1", gateway.LastCourseId);
    }

    private sealed class FakeGateway : IMoodleCoursesGateway
    {
        public string LastUserExternalId { get; private set; } = string.Empty;

        public int LastLimit { get; private set; }

        public string LastQuery { get; private set; } = string.Empty;

        public string LastCourseId { get; private set; } = string.Empty;

        public Task<IReadOnlyList<CourseSummary>> GetMyCoursesAsync(
            string userExternalId,
            int limit,
            CancellationToken cancellationToken)
        {
            LastUserExternalId = userExternalId;
            LastLimit = limit;

            IReadOnlyList<CourseSummary> response =
            [
                CreateCourseSummary()
            ];

            return Task.FromResult(response);
        }

        public Task<IReadOnlyList<CourseSummary>> SearchMyCoursesAsync(
            string userExternalId,
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            LastUserExternalId = userExternalId;
            LastQuery = query;
            LastLimit = limit;

            IReadOnlyList<CourseSummary> response = [CreateCourseSummary()];
            return Task.FromResult(response);
        }

        public Task<CourseSummary?> GetMyCourseAsync(
            string userExternalId,
            string courseId,
            CancellationToken cancellationToken)
        {
            LastUserExternalId = userExternalId;
            LastCourseId = courseId;
            return Task.FromResult<CourseSummary?>(CreateCourseSummary());
        }

        private static CourseSummary CreateCourseSummary()
        {
            return new CourseSummary(
                "1",
                "ID-1",
                "CURSO-TESTE",
                "Curso Teste",
                "Curso Teste",
                10,
                "Categoria",
                null,
                null,
                true,
                null,
                null,
                null,
                null,
                null,
                null);
        }
    }
}
