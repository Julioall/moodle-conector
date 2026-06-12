namespace MoodleConnector.Domain;

public sealed class PendingMoodleAction
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string ToolName { get; init; } = string.Empty;

    public ToolRiskLevel RiskLevel { get; init; }

    public string CreatedBySubject { get; init; } = string.Empty;

    public string? CreatedByEmail { get; init; }

    public long? CreatedByMoodleUserId { get; init; }

    public long? CourseId { get; init; }

    public string PayloadJson { get; init; } = "{}";

    public string PreviewJson { get; init; } = "{}";

    public string ConfirmationText { get; init; } = string.Empty;

    public PendingActionStatus Status { get; private set; } = PendingActionStatus.PendingConfirmation;

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public string? ConfirmedBySubject { get; private set; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public void MarkExpired()
    {
        if (Status == PendingActionStatus.PendingConfirmation)
        {
            Status = PendingActionStatus.Expired;
        }
    }

    public void Confirm(string confirmedBySubject, DateTimeOffset confirmedAt)
    {
        ConfirmedBySubject = confirmedBySubject;
        ConfirmedAt = confirmedAt;
        Status = PendingActionStatus.Confirmed;
    }
}
