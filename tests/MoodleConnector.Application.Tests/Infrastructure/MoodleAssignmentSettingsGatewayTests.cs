using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleAssignmentSettingsGatewayTests
{
    [Fact]
    public async Task StubSemEscalaNaoFabricaMaximoNumerico()
    {
        var gateway = new MoodleAssignmentSettingsGateway(
            Options.Create(new MoodleApiOptions { UseStubData = true }),
            credentialsProvider: null!,
            restClient: null!,
            new MemoryCache(new MemoryCacheOptions()));

        var result = await gateway.GetAssignmentSettingsAsync(
            "teacher",
            "101",
            "5001",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.MaxGrade);
    }
}
