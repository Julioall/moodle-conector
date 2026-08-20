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
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? NextSyncAt { get; set; }
    public string? LastError { get; set; }
    public int RecordsSynced { get; set; }
}
