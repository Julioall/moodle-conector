namespace MoodleConnector.Presentation.Configuration;

public sealed class ConnectorRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int WindowSeconds { get; init; } = 60;

    public int AppAuthPermitLimit { get; init; } = 12;

    public int AdminApiPermitLimit { get; init; } = 30;

    public int McpPermitLimit { get; init; } = 120;
}

