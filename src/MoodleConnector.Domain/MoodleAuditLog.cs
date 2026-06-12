namespace MoodleConnector.Domain;

public sealed class MoodleAuditLog
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string CorrelationId { get; init; } = string.Empty;

    public string ToolName { get; init; } = string.Empty;

    public ToolRiskLevel RiskLevel { get; init; }

    public string ActorSubject { get; init; } = string.Empty;

    public string? ActorEmail { get; init; }

    public long? ActorMoodleUserId { get; init; }

    public long? CourseId { get; init; }

    public string? MoodleFunction { get; init; }

    public string RequestSanitizedJson { get; init; } = "{}";

    public string ResponseSummaryJson { get; init; } = "{}";

    public string Status { get; init; } = string.Empty;

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
