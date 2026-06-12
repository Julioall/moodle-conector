using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Activities;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Application.Tests.Tools.Activities;

public class MoodleCourseActivitiesToolsTests
{
    [Fact]
    public async Task Deve_listar_atividades_sem_campos_de_submissao_ou_nota()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var sut = new MoodleCourseActivitiesTools(mediator, selection, new FakeMoodleUserResolver(777));

        var result = await sut.ListarAtividadesCursoAsync("CURSO", incluirOcultas: true, moodleAlias: "goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);
        Assert.NotNull(mediator.LastActivitiesQuery);
        Assert.Equal("777", mediator.LastActivitiesQuery!.UserExternalId);
        Assert.Equal(CourseActivityModuleTypes.All, mediator.LastActivitiesQuery.ActivityTypes);
        Assert.True(mediator.LastActivitiesQuery.IncludeHidden);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("123", data.GetProperty("courseId").GetString());
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        var activity = data.GetProperty("activities")[0];
        Assert.Equal("assign", activity.GetProperty("activityType").GetString());
        Assert.True(activity.GetProperty("hasDeadline").GetBoolean());
        Assert.False(activity.TryGetProperty("submissionCount", out _));
        Assert.False(activity.TryGetProperty("grade", out _));
        Assert.False(activity.TryGetProperty("attempts", out _));
    }

    [Fact]
    public async Task Deve_listar_tarefas_quizzes_e_scorms_com_filtros_corretos()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleCourseActivitiesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        await sut.ListarTarefasCursoAsync("CURSO");
        Assert.Equal(["assign"], mediator.LastActivitiesQuery!.ActivityTypes);

        await sut.ListarQuizzesCursoAsync("CURSO");
        Assert.Equal(["quiz"], mediator.LastActivitiesQuery!.ActivityTypes);

        await sut.ListarScormsCursoAsync("CURSO");
        Assert.Equal(["scorm"], mediator.LastActivitiesQuery!.ActivityTypes);
    }

    [Fact]
    public async Task Deve_consultar_tarefa_por_identificador()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleCourseActivitiesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ConsultarTarefaAsync("CURSO", "11");

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastActivityQuery);
        Assert.Equal("11", mediator.LastActivityQuery!.ActivityId);
        Assert.Equal(["assign"], mediator.LastActivityQuery.AllowedActivityTypes);
    }

    [Fact]
    public async Task Deve_consultar_prazos_e_sinalizar_atividade_sem_data()
    {
        var mediator = new FakeMediator { ReturnActivityWithoutDates = true };
        var sut = new MoodleCourseActivitiesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ConsultarPrazosAtividadesAsync("CURSO");

        Assert.False(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal(1, data.GetProperty("withoutDatesCount").GetInt32());
        Assert.Equal(1, data.GetProperty("withoutDeadlineCount").GetInt32());
        Assert.False(data.GetProperty("deadlines")[0].GetProperty("hasDates").GetBoolean());
    }

    [Fact]
    public async Task Deve_retornar_lista_vazia_quando_curso_nao_tiver_atividades()
    {
        var mediator = new FakeMediator { ReturnEmptyActivities = true };
        var sut = new MoodleCourseActivitiesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarAtividadesCursoAsync("CURSO");

        Assert.False(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(0, structured.GetProperty("data").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Deve_retornar_erro_controlado_quando_moodle_negar_atividades()
    {
        var mediator = new FakeMediator { ThrowOnActivities = true };
        var sut = new MoodleCourseActivitiesTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarAtividadesCursoAsync("CURSO");

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("Nao foi possivel listar atividades no Moodle neste momento.", structured.GetProperty("warnings")[0].GetString());
    }

    private sealed class FakeMoodleConnectionSelection : IMoodleConnectionSelection
    {
        public string? Alias { get; set; }
    }

    private sealed class FakeMoodleUserResolver(long? userId) : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(userId);
        }
    }

    private sealed class FakeMediator : IMediator
    {
        public ListCourseActivitiesQuery? LastActivitiesQuery { get; private set; }

        public GetCourseActivityQuery? LastActivityQuery { get; private set; }

        public ListActivityDeadlinesQuery? LastDeadlinesQuery { get; private set; }

        public bool ReturnEmptyActivities { get; init; }

        public bool ReturnActivityWithoutDates { get; init; }

        public bool ThrowOnActivities { get; init; }

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
            if (request is ListCourseActivitiesQuery activities)
            {
                LastActivitiesQuery = activities;
                if (ThrowOnActivities)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                return Task.FromResult((TResponse)(object)CreateActivities());
            }

            if (request is GetCourseActivityQuery activity)
            {
                LastActivityQuery = activity;
                return Task.FromResult((TResponse)(object)CreateActivity(ReturnActivityWithoutDates));
            }

            if (request is ListActivityDeadlinesQuery deadlines)
            {
                LastDeadlinesQuery = deadlines;
                return Task.FromResult((TResponse)(object)CreateDeadlines(ReturnActivityWithoutDates));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is ListCourseActivitiesQuery activities)
            {
                LastActivitiesQuery = activities;
                if (ThrowOnActivities)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                return Task.FromResult<object?>(CreateActivities());
            }

            if (request is GetCourseActivityQuery activity)
            {
                LastActivityQuery = activity;
                return Task.FromResult<object?>(CreateActivity(ReturnActivityWithoutDates));
            }

            if (request is ListActivityDeadlinesQuery deadlines)
            {
                LastDeadlinesQuery = deadlines;
                return Task.FromResult<object?>(CreateDeadlines(ReturnActivityWithoutDates));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();

        private CourseActivitiesSummary CreateActivities()
        {
            var activities = ReturnEmptyActivities ? [] : new[] { CreateActivity(ReturnActivityWithoutDates) };
            return new CourseActivitiesSummary(
                "123",
                CourseActivityModuleTypes.All,
                IncludeHidden: false,
                activities.Length,
                activities.Count(activity => !activity.HasDates),
                activities.Count(activity => !activity.HasDeadline),
                activities);
        }

        private static CourseActivityDeadlinesSummary CreateDeadlines(bool withoutDates)
        {
            var activity = CreateActivity(withoutDates);
            return new CourseActivityDeadlinesSummary(
                "123",
                CourseActivityModuleTypes.All,
                IncludeHidden: false,
                Total: 1,
                withoutDates ? 1 : 0,
                activity.HasDeadline ? 0 : 1,
                [new CourseActivityDeadlineSummary(
                    activity.ActivityId,
                    activity.InstanceId,
                    activity.ActivityType,
                    activity.Name,
                    activity.Visible,
                    activity.UserVisible,
                    activity.HasDates,
                    activity.HasDeadline,
                    activity.OpenAt,
                    activity.DueAt,
                    activity.CloseAt,
                    activity.Dates)]);
        }

        private static CourseActivitySummary CreateActivity(bool withoutDates)
        {
            var dates = withoutDates
                ? Array.Empty<CourseModuleDate>()
                :
                [
                    new CourseModuleDate("Entrega ate", new DateTimeOffset(2026, 6, 10, 23, 59, 0, TimeSpan.Zero))
                ];

            return new CourseActivitySummary(
                "11",
                "501",
                "assign",
                "Tarefa 1",
                "https://moodle.example/mod/assign/view.php?id=11",
                true,
                true,
                "Descricao",
                null,
                dates.Length > 0,
                !withoutDates,
                OpenAt: null,
                withoutDates ? null : new DateTimeOffset(2026, 6, 10, 23, 59, 0, TimeSpan.Zero),
                CloseAt: null,
                dates,
                FileCount: 0);
        }
    }
}
