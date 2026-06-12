using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Application.Tests.Tools.Submissions;

public class MoodleAssignmentSubmissionsToolsTests
{
    [Fact]
    public async Task Deve_listar_entregas_sem_texto_integral_ou_nota()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var sut = new MoodleAssignmentSubmissionsTools(mediator, selection, new FakeMoodleUserResolver(777));

        var result = await sut.ListarEntregasAtividadeAsync(
            "CURSO",
            "11",
            status: "entregues",
            moodleAlias: "goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);
        Assert.NotNull(mediator.LastListQuery);
        Assert.Equal("777", mediator.LastListQuery!.UserExternalId);
        Assert.Equal("11", mediator.LastListQuery.AssignmentId);
        Assert.Equal(AssignmentSubmissionFilter.Submitted, mediator.LastListQuery.Filter);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        var submission = data.GetProperty("submissions")[0];
        Assert.Equal("101", submission.GetProperty("userId").GetString());
        Assert.True(submission.GetProperty("submitted").GetBoolean());
        Assert.True(submission.GetProperty("hasOnlineText").GetBoolean());
        Assert.False(submission.TryGetProperty("content", out _));
        Assert.False(submission.TryGetProperty("submissionText", out _));
        Assert.False(submission.TryGetProperty("grade", out _));
    }

    [Fact]
    public async Task Deve_listar_pendentes_atrasadas_e_aguardando_correcao()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleAssignmentSubmissionsTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        await sut.ListarEntregasPendentesAsync("CURSO", "11");
        Assert.Equal(AssignmentSubmissionFilter.NotSubmitted, mediator.LastListQuery!.Filter);

        await sut.ListarEntregasAtrasadasAsync("CURSO", "11");
        Assert.Equal(AssignmentSubmissionFilter.Late, mediator.LastListQuery!.Filter);

        await sut.ListarEntregasAguardandoCorrecaoAsync("CURSO", "11");
        Assert.Equal(AssignmentSubmissionFilter.NeedsGrading, mediator.LastListQuery!.Filter);
    }

    [Fact]
    public async Task Deve_consultar_entrega_de_aluno()
    {
        var mediator = new FakeMediator();
        var sut = new MoodleAssignmentSubmissionsTools(
            mediator,
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ConsultarEntregaAlunoAsync("CURSO", "11", "101");

        Assert.False(result.IsError ?? false);
        Assert.NotNull(mediator.LastStudentQuery);
        Assert.Equal("101", mediator.LastStudentQuery!.StudentId);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("submitted", structured.GetProperty("data").GetProperty("submission").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deve_rejeitar_status_invalido()
    {
        var sut = new MoodleAssignmentSubmissionsTools(
            new FakeMediator(),
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarEntregasAtividadeAsync("CURSO", "11", status: "desconhecido");

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("error", structured.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deve_retornar_erro_controlado_quando_moodle_falhar()
    {
        var sut = new MoodleAssignmentSubmissionsTools(
            new FakeMediator { ThrowOnList = true },
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.ListarEntregasAtividadeAsync("CURSO", "11");

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("Nao foi possivel listar entregas no Moodle neste momento.", structured.GetProperty("warnings")[0].GetString());
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
        public ListAssignmentSubmissionsQuery? LastListQuery { get; private set; }

        public GetStudentSubmissionQuery? LastStudentQuery { get; private set; }

        public bool ThrowOnList { get; init; }

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
            if (request is ListAssignmentSubmissionsQuery list)
            {
                LastListQuery = list;
                if (ThrowOnList)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                return Task.FromResult((TResponse)(object)CreatePage(list));
            }

            if (request is GetStudentSubmissionQuery student)
            {
                LastStudentQuery = student;
                return Task.FromResult((TResponse)(object)CreateSubmittedSummary());
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is ListAssignmentSubmissionsQuery list)
            {
                LastListQuery = list;
                if (ThrowOnList)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                return Task.FromResult<object?>(CreatePage(list));
            }

            if (request is GetStudentSubmissionQuery student)
            {
                LastStudentQuery = student;
                return Task.FromResult<object?>(CreateSubmittedSummary());
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();

        private static AssignmentSubmissionsPage CreatePage(ListAssignmentSubmissionsQuery query)
        {
            var summary = query.Filter switch
            {
                AssignmentSubmissionFilter.NotSubmitted => new AssignmentSubmissionSummary(
                    "102",
                    "Bruno Lima",
                    SubmissionId: null,
                    "not_submitted",
                    GradingStatus: null,
                    Submitted: false,
                    Late: false,
                    NeedsGrading: false,
                    SubmittedAt: null,
                    ModifiedAt: null,
                    AttemptNumber: null,
                    FileCount: 0,
                    HasOnlineText: false),
                AssignmentSubmissionFilter.Late => CreateSubmittedSummary(late: true, needsGrading: false),
                AssignmentSubmissionFilter.NeedsGrading => CreateSubmittedSummary(late: false, needsGrading: true),
                _ => CreateSubmittedSummary()
            };

            return new AssignmentSubmissionsPage(
                "123",
                "501",
                "11",
                "Tarefa 1",
                query.Page,
                query.PageSize,
                query.Filter,
                query.IncludeLate,
                query.IncludeUngraded,
                query.Since,
                query.Before,
                Total: 1,
                HasMore: false,
                [summary]);
        }

        private static AssignmentSubmissionSummary CreateSubmittedSummary(
            bool late = false,
            bool needsGrading = false)
        {
            return new AssignmentSubmissionSummary(
                "101",
                "Ana Souza",
                "9001",
                "submitted",
                needsGrading ? "notgraded" : "graded",
                Submitted: true,
                late,
                needsGrading,
                SubmittedAt: new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero),
                ModifiedAt: new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero),
                AttemptNumber: 0,
                FileCount: 1,
                HasOnlineText: true);
        }
    }
}
