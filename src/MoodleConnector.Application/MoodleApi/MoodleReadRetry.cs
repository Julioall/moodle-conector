namespace MoodleConnector.Application.MoodleApi;

/// <summary>
/// Short retry policy for idempotent Moodle reads. It deliberately excludes
/// permission, authentication, missing-function and invalid-response errors.
/// Callers must not use it around Moodle writes or local resource mutations.
/// </summary>
public static class MoodleReadRetry
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
            catch (Exception exception) when (IsTransient(exception))
            {
                if (attempt >= MaxAttempts)
                {
                    // Preserve the original transport/Moodle error so callers
                    // can still return its structured error code and audit id.
                    throw;
                }

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
            _ => false,
        };
    }
}
