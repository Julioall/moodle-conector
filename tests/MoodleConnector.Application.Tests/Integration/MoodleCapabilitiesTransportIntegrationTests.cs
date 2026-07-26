using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Integration;

public sealed class MoodleCapabilitiesTransportIntegrationTests
{
    [Fact]
    public async Task Catalog_ObtemPerfisDiferentesDeDoisMoodlesSimuladosPeloRestClient()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.Run(async context =>
        {
            Assert.Equal("/webservice/rest/server.php", context.Request.Path);
            var form = await context.Request.ReadFormAsync();
            Assert.Equal("core_webservice_get_site_info", form["wsfunction"]);
            Assert.DoesNotContain("wstoken", context.Request.QueryString.Value, StringComparison.OrdinalIgnoreCase);

            var response = form["wstoken"] == "token-goias"
                ? "{\"sitename\":\"GoiÃ¡s\",\"release\":\"4.5\",\"userid\":7,\"functions\":[{\"name\":\"core_course_get_courses_by_field\"},{\"name\":\"core_course_get_enrolled_courses_by_timeline_classification\"},{\"name\":\"mod_assign_get_assignments\"}]}"
                : "{\"sitename\":\"Nacional\",\"release\":\"5.1.2\",\"userid\":8,\"functions\":[{\"name\":\"core_enrol_get_users_courses\"},{\"name\":\"mod_assign_get_assignments\"},{\"name\":\"mod_assign_get_submissions\"}]}";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(response, Encoding.UTF8);
        });
        await app.StartAsync();

        var credentials = new SwitchingCredentialsProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var httpClient = app.GetTestClient();
        var catalog = new MoodleFunctionCatalog(
            cache,
            new MoodleRestClient(httpClient, Options.Create(new MoodleApiOptions()), new SwitchingTokenProvider(credentials)),
            credentials);

        credentials.Current = "goias";
        var goias = await catalog.GetCurrentAsync(false, CancellationToken.None);
        credentials.Current = "nacional";
        var nacional = await catalog.GetCurrentAsync(false, CancellationToken.None);

        Assert.Equal("4.5", goias.Release);
        Assert.Contains(goias.Functions, function => function.Name == "core_course_get_enrolled_courses_by_timeline_classification");
        Assert.Equal("5.1.2", nacional.Release);
        Assert.Contains(nacional.Functions, function => function.Name == "core_enrol_get_users_courses");

        var flows = new MoodleBusinessFlowRegistry();
        Assert.Equal("timeline", flows.Evaluate("listar_cursos_ativos", goias).SelectedStrategy);
        Assert.Equal("enrolled_courses_fallback", flows.Evaluate("listar_cursos_ativos", nacional).SelectedStrategy);
        Assert.True(flows.Evaluate("listar_entregas_aguardando_correcao", nacional).IsAvailable);
    }

    private sealed class SwitchingCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public string Current { get; set; } = "goias";

        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials("client", Current, Current, "http://localhost", "user", "password", Current, false));
    }

    private sealed class SwitchingTokenProvider(SwitchingCredentialsProvider credentials) : IMoodleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(credentials.Current == "goias" ? "token-goias" : "token-nacional");
    }
}
