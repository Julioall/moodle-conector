namespace MoodleConnector.Infrastructure;

public sealed class ReportJobEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ConnectionAlias { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string? CategoryPath { get; set; }
    public string? CourseId { get; set; }
    public string? CourseIdsJson { get; set; }
    public string? CourseNamesJson { get; set; }
    public string Status { get; set; } = "queued";
    public int ProgressPercent { get; set; }
    public int TotalCourses { get; set; }
    public int ProcessedCourses { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public string? ContentText { get; set; }
    public string? ContentBase64 { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
