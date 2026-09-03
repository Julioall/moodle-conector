using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleGradebookGatewayCachingTests
{
    [Fact]
    public async Task Reuses_the_daily_gradebook_snapshot_for_the_same_course_and_student()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var restClient = new CountingRestClient();
        var gateway = new MoodleGradebookGateway(
            Options.Create(new MoodleApiOptions()),
            new CredentialsProvider(),
            restClient,
            cache);

        var first = await gateway.GetStudentGradebookAsync("42", "7", CancellationToken.None);
        var second = await gateway.GetStudentGradebookAsync("42", "7", CancellationToken.None);

        Assert.Equal(1, restClient.Calls);
        Assert.Single(first.Items);
        Assert.Single(second.Items);
        Assert.Equal(1787075877, first.Items.Single().GradedDateGraded);
        Assert.Equal("117487", first.Items.Single().ItemInstance);
        Assert.Equal("1108049", first.Items.Single().CourseModuleId);
    }

    private sealed class CredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials(
                "client", "connection", "demo", "https://moodle.example", "user", "password", "demo", false));
    }

    private sealed class CountingRestClient : IMoodleRestClient
    {
        public int Calls { get; private set; }

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) =>
            CallAsync(connection, functionName, parameters, allowServiceToken: true, cancellationToken);

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken)
        {
            Calls++;
            using var document = JsonDocument.Parse("""
                {"usergrades":[{"gradeitems":[{"id":1,"itemname":"SA 1","itemtype":"mod","itemmodule":"assign","iteminstance":117487,"cmid":1108049,"graderaw":8,"grademin":0,"grademax":10,"gradedategraded":1787075877}]}]}
                """);
            return Task.FromResult(document.RootElement.Clone());
        }
    }
}
