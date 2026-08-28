using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Retry curto para leituras Moodle usadas pelo worker. Não repete erros de
/// permissão, autenticação, função ausente ou resposta inválida.
/// </summary>
internal static class GradingMoodleReadRetry
{
    private const int MaxAttempts = 3;

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Action<Exception, int>? onRetry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < MaxAttempts)
            {
                onRetry?.Invoke(exception, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("A leitura Moodle nao foi concluida.");
    }

    public static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => true,
            TimeoutException => true,
            TaskCanceledException => true,
            MoodleApiException moodle =>
                MoodleErrorContract.NormalizeCode(moodle.ErrorCode) is
                    MoodleErrorContract.NetworkError or MoodleErrorContract.RequestTimeout,
            _ => false
        };
    }
}
