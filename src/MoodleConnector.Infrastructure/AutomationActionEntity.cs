namespace MoodleConnector.Infrastructure;

public sealed class AutomationActionEntity
{
    public Guid Id { get; set; }
    public Guid AutomationId { get; set; }
    public Guid RunId { get; set; }
    public Guid OwnerId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public string Status { get; set; } = "created";
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
