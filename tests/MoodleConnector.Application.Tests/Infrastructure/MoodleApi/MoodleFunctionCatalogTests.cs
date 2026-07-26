using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure.MoodleApi;

public sealed class MoodleFunctionCatalogTests
{
    [Fact]
    public async Task GetCurrentAsync_MantemPerfisIndependentesPorConexao()
    {
        var credentials = new SwitchingCredentialsProvider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var catalog = new MoodleFunctionCatalog(cache, new ProfileRestClient(), credentials);

        credentials.Current = "goias";
        var goias = await catalog.GetCurrentAsync(false, CancellationToken.None);
        credentials.Current = "nacional";
        var nacional = await catalog.GetCurrentAsync(false, CancellationToken.None);

        Assert.Equal("4.5", goias.Release);
        Assert.Contains(goias.Functions, function => function.Name == "core_enrol_get_users_courses");
        Assert.DoesNotContain(goias.Functions, function => function.Name == "core_course_get_enrolled_courses_by_timeline_classification");
        Assert.Equal("5.1.2", nacional.Release);
        Assert.Contains(nacional.Functions, function => function.Name == "core_course_get_enrolled_courses_by_timeline_classification");
    }

    private sealed class SwitchingCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public string Current { get; set; } = "goias";

        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", Current, Current, "https://moodle.example", "user", "password", Current, false));
    }

    private sealed class ProfileRestClient : IMoodleRestClient
    {
        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, true, cancellationToken);

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, bool allowServiceToken, CancellationToken cancellationToken)
        {
            var payload = connection.ConnectionId == "goias"
                ? "{\"sitename\":\"GoiÃ¡s\",\"release\":\"4.5\",\"userid\":7,\"functions\":[{\"name\":\"core_enrol_get_users_courses\"}]}"
                : "{\"sitename\":\"Nacional\",\"release\":\"5.1.2\",\"userid\":8,\"functions\":[{\"name\":\"core_course_get_enrolled_courses_by_timeline_classification\"}]}";
            using var document = JsonDocument.Parse(payload);
            return Task.FromResult(document.RootElement.Clone());
        }
    }
}
