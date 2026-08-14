using MoodleConnector.Application.Automation;

namespace MoodleConnector.Presentation;

public sealed record AppAutomationInput(
    string? ConnectionAlias,
    string CourseId,
    string Name,
    string? Description,
    string ScheduleType,
    int RunHourUtc,
    int RunMinuteUtc,
    int? RunDayOfWeek,
    string ConditionType,
    string ActionType,
    AutomationRuleConfig? Config,
    bool IsEnabled = true);

public sealed record AppAutomationRunInput(bool Force = false);

public sealed record AppAutomationDto(
    Guid Id,
    Guid OwnerId,
    string? ConnectionAlias,
    string CourseId,
    string Name,
    string? Description,
    string ScheduleType,
    int RunHourUtc,
    int RunMinuteUtc,
    int? RunDayOfWeek,
    string ConditionType,
    string ActionType,
    AutomationRuleConfig Config,
    bool IsEnabled,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AppAutomationRunDto(
    Guid RunId,
    Guid AutomationId,
    string Status,
    string Trigger,
    int AttemptCount,
    int CreatedActions,
    int SkippedActions,
    int FailedActions,
    IReadOnlyList<Guid> PendingActionIds,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? SummaryJson = null);
