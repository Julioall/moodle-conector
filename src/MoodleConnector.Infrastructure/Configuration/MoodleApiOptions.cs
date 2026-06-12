namespace MoodleConnector.Infrastructure;

public sealed class MoodleApiOptions
{
    public const string SectionName = "MoodleApi";

    public string BaseUrl { get; init; } = string.Empty;

    public string ServiceToken { get; init; } = string.Empty;

    public string? WriteServiceToken { get; init; }

    public string LoginService { get; init; } = "moodle_mobile_app";

    public bool AllowServiceTokenForReadOnlyQueries { get; init; } = false;

    public bool UseStubData { get; init; } = false;

    public int HttpTimeoutSeconds { get; init; } = 30;

    public int HttpRetryCount { get; init; } = 2;

    public int CircuitBreakerHandledEventsAllowedBeforeBreaking { get; init; } = 5;

    public int CircuitBreakerDurationSeconds { get; init; } = 30;
}
