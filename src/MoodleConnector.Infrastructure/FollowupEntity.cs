namespace MoodleConnector.Infrastructure;

public sealed class FollowupEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string StudentRef { get; set; } = string.Empty;
    public string? CourseRef { get; set; }
    public string Kind { get; set; } = "acompanhamento";
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
