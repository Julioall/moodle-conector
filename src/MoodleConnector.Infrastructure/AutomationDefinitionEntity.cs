namespace MoodleConnector.Infrastructure;

public sealed class AutomationDefinitionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string? ConnectionAlias { get; set; }
    public string CourseId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ScheduleType { get; set; } = "daily";
    public int RunHourUtc { get; set; } = 8;
    public int RunMinuteUtc { get; set; }
    public int? RunDayOfWeek { get; set; }
    public string ConditionType { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
