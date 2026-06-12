namespace MoodleConnector.Application.PendingActions;

public sealed class PendingActionOptions
{
    public const string SectionName = "MoodleConnector";

    public int PendingActionExpirationMinutes { get; init; } = 15;
}
