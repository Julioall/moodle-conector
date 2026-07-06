using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Participants;

public sealed class MoodleParticipantsGatewayTests
{
    [Fact]
    public async Task Solicita_roles_e_groups_e_preserva_grupos()
    {
        var handler = new JsonHandler("""
            [{"id":123,"fullname":"Aluno","suspended":false,"roles":[{"roleid":5,"shortname":"student","name":"Estudante"}],"groups":[{"id":9,"name":"Turma A"}]}]
            """);
        var sut = CreateGateway(handler);

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, false, false, null, CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.LastRequestUri!.Query);
        Assert.Contains("roles", query);
        Assert.Contains("groups", query);
        Assert.Equal("Turma A", Assert.Single(result.Participants).Groups[0].Name);
    }

    [Fact]
    public async Task Inclui_participante_sem_roles_quando_students_only()
    {
        var sut = CreateGateway(new JsonHandler("""
            [{"id":123,"fullname":"Possivel aluno","suspended":false,"roles":[],"groups":[]}]
            """));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, true, false, null, CancellationToken.None);

        Assert.Single(result.Participants);
        Assert.NotNull(result.ClassificationDiagnostics);
        Assert.Equal(1, result.ClassificationDiagnostics.IncludedByFallbackCount);
        Assert.True(result.ClassificationDiagnostics.HasEmptyRoles);
        Assert.Equal(ParticipantClassificationMode.Fallback, result.ClassificationDiagnostics.Mode);
    }

    [Fact]
    public async Task Exclui_somente_perfil_conhecido_de_equipe()
    {
        var sut = CreateGateway(new JsonHandler("""
            [
              {"id":1,"fullname":"Professor","suspended":false,"roles":[{"roleid":3,"shortname":"editingteacher","name":"Professor"}],"groups":[]},
              {"id":2,"fullname":"Papel local","suspended":false,"roles":[{"roleid":8,"shortname":"local","name":"Papel local"}],"groups":[]}
            ]
            """));

        var result = await sut.GetCourseParticipantsAsync(
            "42", "10", ParticipantStatusFilter.All, 1, 20, true, false, null, CancellationToken.None);

        Assert.Equal("Papel local", Assert.Single(result.Participants).FullName);
        Assert.Equal(1, result.ClassificationDiagnostics!.ExcludedKnownStaffCount);
        Assert.Equal(1, result.ClassificationDiagnostics.IncludedByFallbackCount);
    }

    private static MoodleParticipantsGateway CreateGateway(JsonHandler handler)
    {
        return new MoodleParticipantsGateway(
            new HttpClient(handler),
            Options.Create(new MoodleApiOptions()),
            new FakeTokenProvider(),
            new FakeCredentialsProvider());
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeTokenProvider : IMoodleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("token");
    }

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", "connection", "default", "https://moodle.example", "user", "password", "target", false));
    }
}
