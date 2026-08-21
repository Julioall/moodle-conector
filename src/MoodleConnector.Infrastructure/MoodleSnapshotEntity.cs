namespace MoodleConnector.Infrastructure;

public sealed class MoodleSnapshotEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string ConnectionAlias { get; set; } = string.Empty;
    public string SnapshotType { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Tier { get; set; } = "hot";
    public bool IsFrozen { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FreshUntil { get; set; }
    public DateTimeOffset? StaleUntil { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? PayloadHash { get; set; }
    public bool IsComplete { get; set; } = true;
    public int RecordCount { get; set; }
}

public sealed class MoodleSyncStateEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string ConnectionAlias { get; set; } = string.Empty;
    public string Dataset { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? NextSyncAt { get; set; }
    public string? LastError { get; set; }
    public int RecordsSynced { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string UserExternalId { get; set; } = string.Empty;
    public int Priority { get; set; } = 50;
    public DateTimeOffset? LeaseUntil { get; set; }
    public int AttemptCount { get; set; }
    public bool ForceRequested { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
