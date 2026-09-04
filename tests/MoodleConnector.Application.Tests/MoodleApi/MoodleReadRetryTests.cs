using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Application.Tests.MoodleApi;

public sealed class MoodleReadRetryTests
{
    [Fact]
    public async Task Repete_leitura_transitoria_e_retorna_quando_a_proxima_tentativa_funciona()
    {
        var calls = 0;
        var retries = 0;

        var result = await MoodleReadRetry.ExecuteAsync(
            _ =>
            {
                calls++;
                if (calls < 3)
                {
                    throw new HttpRequestException("transient");
                }

                return Task.FromResult("ok");
            },
            (_, _) => retries++,
            CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(3, calls);
        Assert.Equal(2, retries);
    }

    [Fact]
    public async Task Nao_repete_erro_de_permissao()
    {
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MoodleReadRetry.ExecuteAsync<string>(
                _ =>
                {
                    calls++;
                    throw new InvalidOperationException("permission denied");
                },
                null,
                CancellationToken.None));

        Assert.Equal(1, calls);
    }
}
