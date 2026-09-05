using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Participants;

public sealed class MoodleParticipantsGatewayTests
{
    [Fact]
    public async Task Solicita_roles_e_groups_e_preserva_grupos()
    {
        var handler = new JsonHandler("""
            [{"id":123,"fullname":"Aluno","suspended":false,"roles":[{"roleid":5,"shortname":"student","name":"Estudante"}],"groups":[{"id":9,"name":"Turma A"}]}]
            """, "[]");
        var sut = CreateGateway(handler);

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, false, false, null, CancellationToken.None);

        var body = Uri.UnescapeDataString(handler.LastRequestBody);
        Assert.Contains("roles", body);
        Assert.Contains("groups", body);
        Assert.DoesNotContain("token", handler.LastRequestUri!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Turma A", Assert.Single(result.Participants).Groups[0].Name);
    }

    [Fact]
    public async Task Usa_onlyactive_do_Moodle_para_consultar_matriculas_ativas()
    {
        var handler = new JsonHandler(
            "[{\"id\":123,\"fullname\":\"Aluno\",\"suspended\":false,\"roles\":[],\"groups\":[]}]" );
        var sut = CreateGateway(handler);

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.Active, 1, 20, false, false, null, CancellationToken.None);

        var body = Uri.UnescapeDataString(handler.LastRequestBody);
        Assert.Contains("onlyactive", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[value]=1", body, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.Participants);
        Assert.Equal("active", result.Participants[0].EnrollmentStatus);
    }

    [Fact]
    public async Task Filtra_grupo_localmente_quando_Moodle_rejeita_parametro_groupid()
    {
        var handler = new JsonHandler(
            "{\"exception\":\"invalid_parameter_exception\",\"errorcode\":\"invalidparameter\",\"message\":\"groupid nao suportado\"}",
            "[{\"id\":123,\"fullname\":\"Aluno\",\"suspended\":false,\"roles\":[],\"groups\":[{\"id\":9,\"name\":\"Turma A\"}]}]");
        var sut = CreateGateway(handler);

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.Active, 1, 20, false, false, "9", CancellationToken.None);

        Assert.Single(result.Participants);
        Assert.Equal("123", result.Participants[0].UserId);
        Assert.DoesNotContain("groupid", Uri.UnescapeDataString(handler.LastRequestBody), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Usa_onlysuspended_do_Moodle_mesmo_quando_campo_suspended_nao_esta_disponivel()
    {
        var handler = new JsonHandler("[{\"id\":123,\"fullname\":\"Aluno suspenso\",\"roles\":[],\"groups\":[]}]");
        var sut = CreateGateway(handler);

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.Suspended, 1, 20, false, false, null, CancellationToken.None);

        var body = Uri.UnescapeDataString(handler.LastRequestBody);
        Assert.Contains("onlysuspended", body, StringComparison.OrdinalIgnoreCase);
        var participant = Assert.Single(result.Participants);
        Assert.True(participant.Suspended);
        Assert.Equal("suspended", participant.EnrollmentStatus);
    }

    [Fact]
    public async Task Todos_combina_matriculas_ativas_e_suspensas_com_status_individual()
    {
        var sut = CreateGateway(new JsonHandler(
            "[{\"id\":1,\"fullname\":\"Ativo\",\"roles\":[],\"groups\":[]}]",
            "[{\"id\":2,\"fullname\":\"Suspenso\",\"roles\":[],\"groups\":[]}]"));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, false, false, null, CancellationToken.None);

        Assert.Equal(2, result.Participants.Count);
        var active = Assert.Single(result.Participants, item => item.UserId == "1");
        var suspended = Assert.Single(result.Participants, item => item.UserId == "2");
        Assert.False(active.Suspended);
        Assert.Equal("active", active.EnrollmentStatus);
        Assert.True(suspended.Suspended);
        Assert.Equal("suspended", suspended.EnrollmentStatus);
    }

    [Fact]
    public async Task Usa_grupos_embutidos_quando_funcao_de_grupos_e_negada()
    {
        var handler = new JsonHandler(
            "{\"exception\":\"required_capability_exception\",\"errorcode\":\"nopermissions\",\"message\":\"Sem permissao\"}",
            "[{\"id\":123,\"fullname\":\"Aluno\",\"suspended\":false,\"roles\":[],\"groups\":[{\"id\":9,\"name\":\"Turma A\"}]}]");
        var sut = CreateGateway(handler);

        var result = await sut.GetCourseGroupsAsync("42", "10", CancellationToken.None);

        var group = Assert.Single(result);
        Assert.Equal("9", group.GroupId);
        Assert.Equal("10", group.CourseId);
        Assert.Equal("Turma A", group.Name);
    }

    [Fact]
    public async Task Inclui_participante_sem_roles_quando_students_only()
    {
        var sut = CreateGateway(new JsonHandler("""
            [{"id":123,"fullname":"Possivel aluno","suspended":false,"roles":[],"groups":[]}]
            """, "[]"));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, true, false, null, CancellationToken.None);

        Assert.Single(result.Participants);
        Assert.NotNull(result.ClassificationDiagnostics);
        Assert.Equal(1, result.ClassificationDiagnostics.IncludedByFallbackCount);
        Assert.True(result.ClassificationDiagnostics.HasEmptyRoles);
        Assert.Equal(ParticipantClassificationMode.Fallback, result.ClassificationDiagnostics.Mode);
    }

    [Fact]
    public async Task Exclui_registro_roleless_sem_nome_da_lista_de_alunos()
    {
        var sut = CreateGateway(new JsonHandler("""
            [
              {"id":245956,"fullname":null,"suspended":false,"roles":[],"groups":[]},
              {"id":123,"fullname":"Aluno sem role","suspended":false,"roles":[],"groups":[]}
            ]
            """, "[]"));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.Active, 1, 20, true, false, null, CancellationToken.None);

        var participant = Assert.Single(result.Participants);
        Assert.Equal("123", participant.UserId);
    }

    [Fact]
    public async Task Exclui_toda_role_preenchida_que_nao_seja_student()
    {
        var sut = CreateGateway(new JsonHandler("""
            [
              {"id":1,"fullname":"Professor","suspended":false,"roles":[{"roleid":3,"shortname":"editingteacher-go","name":"Professor - GO"}],"groups":[]},
              {"id":2,"fullname":"Monitora","suspended":false,"roles":[{"roleid":8,"shortname":"monitor_go","name":"Monitor - GO"}],"groups":[]},
              {"id":3,"fullname":"Aluna","suspended":false,"roles":[{"roleid":5,"shortname":"student","name":"Estudante"}],"groups":[]}
            ]
            """, "[]"));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, true, false, null, CancellationToken.None);

        Assert.Equal("Aluna", Assert.Single(result.Participants).FullName);
        Assert.Equal(2, result.ClassificationDiagnostics!.ExcludedKnownStaffCount);
        Assert.Equal(0, result.ClassificationDiagnostics.IncludedByFallbackCount);
    }

    [Fact]
    public async Task Usa_populacao_sem_filtro_quando_onlyactive_e_negado_e_preserva_alunos()
    {
        var sut = CreateGateway(new JsonHandler(
            """{"exception":"required_capability_exception","errorcode":"nopermissions","message":"Sem permissao"}""",
            """
            [
              {"id":1,"fullname":"Professor","suspended":false,"roles":[{"roleid":3,"shortname":"editingteacher-go","name":"Professor - GO"}],"groups":[]},
              {"id":2,"fullname":"Aluno ativo","roles":[{"roleid":5,"shortname":"student","name":"Estudante"}],"groups":[]},
              {"id":3,"fullname":"Aluno suspenso","suspended":true,"roles":[{"roleid":5,"shortname":"student","name":"Estudante"}],"groups":[]}
            ]
            """));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.Active, 1, 20, true, false, null, CancellationToken.None);

        var participant = Assert.Single(result.Participants);
        Assert.Equal("2", participant.UserId);
        Assert.Equal("active", participant.EnrollmentStatus);
        Assert.True(result.ClassificationDiagnostics!.UsedStatusFilterFallback);
        Assert.Equal(1, result.ClassificationDiagnostics.ExcludedKnownStaffCount);
    }

    [Fact]
    public async Task Status_all_combina_populacoes_quando_filtros_sao_negados()
    {
        var sut = CreateGateway(new JsonHandler(
            """{"exception":"required_capability_exception","errorcode":"nopermissions","message":"Sem permissao"}""",
            """[{"id":2,"fullname":"Aluno ativo","suspended":false,"roles":[{"roleid":5,"shortname":"student","name":"Estudante"}],"groups":[]}]""",
            """{"exception":"required_capability_exception","errorcode":"nopermissions","message":"Sem permissao"}""",
            """[{"id":3,"fullname":"Aluno suspenso","suspended":true,"roles":[{"roleid":5,"shortname":"student","name":"Estudante"}],"groups":[]}]"""));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, true, false, null, CancellationToken.None);

        Assert.Equal(2, result.Participants.Count);
        Assert.Contains(result.Participants, participant => participant.UserId == "2" && participant.EnrollmentStatus == "active");
        Assert.Contains(result.Participants, participant => participant.UserId == "3" && participant.EnrollmentStatus == "suspended");
        Assert.True(result.ClassificationDiagnostics!.UsedStatusFilterFallback);
    }

    private static MoodleParticipantsGateway CreateGateway(JsonHandler handler)
    {
        return new MoodleParticipantsGateway(
            Options.Create(new MoodleApiOptions()),
            new FakeCredentialsProvider(),
            new MoodleRestClient(
                new HttpClient(handler),
                new FakeTokenProvider(),
                NullLogger<MoodleRestClient>.Instance));
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public JsonHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public Uri? LastRequestUri { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var json = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeTokenProvider : IMoodleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            MoodleConnectorCredentials connection,
            CancellationToken cancellationToken) =>
            Task.FromResult("token");

        public void Invalidate(MoodleConnectorCredentials connection)
        {
        }
    }

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", "connection", "default", "https://moodle.example", "user", "password", "target", false));
    }
}
