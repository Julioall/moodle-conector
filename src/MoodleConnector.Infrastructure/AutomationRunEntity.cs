namespace MoodleConnector.Infrastructure;

public sealed class AutomationRunEntity
{
    public Guid Id { get; set; }
    public Guid AutomationId { get; set; }
    public Guid OwnerId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Trigger { get; set; } = "schedule";
    public string Status { get; set; } = "queued";
    public int AttemptCount { get; set; }
    public DateTimeOffset ScheduledFor { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? SummaryJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
