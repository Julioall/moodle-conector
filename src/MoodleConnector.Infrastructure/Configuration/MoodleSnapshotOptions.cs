namespace MoodleConnector.Infrastructure.Configuration;

public sealed class MoodleSnapshotOptions
{
    public const string SectionName = "MoodleSnapshots";

    public int QueueCapacity { get; init; } = 256;
    public int GlobalConcurrency { get; init; } = 4;
    public int PerConnectionConcurrency { get; init; } = 1;
    public int MaxPayloadBytes { get; init; } = 10 * 1024 * 1024;
    public int CoursePageSize { get; init; } = 100;
    public int MaxCoursePages { get; init; } = 10;
    public int ParticipantPageSize { get; init; } = 1000;
    public int AssignmentBatchSize { get; init; } = 100;
    public int RunRetentionDays { get; init; } = 30;
    public int CleanupIntervalMinutes { get; init; } = 60;
    public int LeaseMinutes { get; init; } = 30;

    public MoodleSnapshotOptions Normalize() => new()
    {
        QueueCapacity = Math.Clamp(QueueCapacity, 16, 10_000),
        GlobalConcurrency = Math.Clamp(GlobalConcurrency, 1, 64),
        PerConnectionConcurrency = Math.Clamp(PerConnectionConcurrency, 1, 16),
        MaxPayloadBytes = Math.Clamp(MaxPayloadBytes, 64 * 1024, 100 * 1024 * 1024),
        CoursePageSize = Math.Clamp(CoursePageSize, 1, 1000),
        MaxCoursePages = Math.Clamp(MaxCoursePages, 1, 100),
        ParticipantPageSize = Math.Clamp(ParticipantPageSize, 1, 1000),
        AssignmentBatchSize = Math.Clamp(AssignmentBatchSize, 1, 500),
        RunRetentionDays = Math.Clamp(RunRetentionDays, 1, 3650),
        CleanupIntervalMinutes = Math.Clamp(CleanupIntervalMinutes, 5, 24 * 60),
        LeaseMinutes = Math.Clamp(LeaseMinutes, 5, 180),
    };
}
