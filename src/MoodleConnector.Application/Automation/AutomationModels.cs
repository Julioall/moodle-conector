using System.Text.Json;

namespace MoodleConnector.Application.Automation;

public sealed class AutomationSchedulerOptions
{
    public const string SectionName = "Automation";

    public bool Enabled { get; init; } = true;
    public int PollIntervalSeconds { get; init; } = 30;
    public int MaxDefinitionsPerTick { get; init; } = 20;
}

public static class AutomationCatalog
{
    public const string ManualSchedule = "manual";
    public const string DailySchedule = "daily";
    public const string WeeklySchedule = "weekly";

    public const string OverdueSubmissionsCondition = "overdue_submissions";
    public const string AwaitingGradingCondition = "awaiting_grading";
    public const string WeeklySignalsCondition = "weekly_signals";

    public const string CreateTasksAction = "create_tasks";
    public const string CreateFollowupsAction = "create_followups";
    public const string PrepareMoodleMessageAction = "prepare_moodle_message";
    public const string FollowupAndPrepareMessageAction = "create_followup_and_prepare_message";
    public const string RecordEvidenceAction = "record_evidence";
    public const string GenerateWeeklySummaryAction = "generate_weekly_summary";

    public static readonly IReadOnlySet<string> Schedules =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManualSchedule, DailySchedule, WeeklySchedule };

    public static readonly IReadOnlySet<string> Conditions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            OverdueSubmissionsCondition,
            AwaitingGradingCondition,
            WeeklySignalsCondition
        };

    public static readonly IReadOnlySet<string> Actions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CreateTasksAction,
            CreateFollowupsAction,
            PrepareMoodleMessageAction,
            FollowupAndPrepareMessageAction,
            RecordEvidenceAction,
            GenerateWeeklySummaryAction
        };
}

public sealed record AutomationRuleConfig(
    int DueDaysAhead = 0,
    int MaxStudentsToAnalyze = 100,
    int MaxAssignmentsToAnalyze = 50,
    int InactivityThresholdDays = 7,
    decimal MinGradePercentage = 60m,
    string? MessageText = null);

public sealed record AutomationDefinitionInput(
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

public sealed record AutomationDefinitionSummary(
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

public sealed record AutomationRunSummary(
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

public interface IAutomationRuntime
{
    Task<IReadOnlyList<AutomationDefinitionSummary>> ListAsync(
        Guid ownerId,
        CancellationToken cancellationToken);

    Task<AutomationDefinitionSummary> CreateAsync(
        Guid ownerId,
        AutomationDefinitionInput input,
        CancellationToken cancellationToken);

    Task<AutomationDefinitionSummary?> UpdateAsync(
        Guid ownerId,
        Guid automationId,
        AutomationDefinitionInput input,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        Guid ownerId,
        Guid automationId,
        CancellationToken cancellationToken);

    Task<AutomationRunSummary> RunAsync(
        Guid ownerId,
        Guid automationId,
        string trigger,
        bool force,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AutomationRunSummary>> ListRunsAsync(
        Guid ownerId,
        Guid automationId,
        int limit,
        CancellationToken cancellationToken);

    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public static class AutomationConfigSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(AutomationRuleConfig config) => JsonSerializer.Serialize(config, Options);

    public static AutomationRuleConfig Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AutomationRuleConfig();
        try
        {
            return JsonSerializer.Deserialize<AutomationRuleConfig>(json, Options) ?? new AutomationRuleConfig();
        }
        catch (JsonException)
        {
            return new AutomationRuleConfig();
        }
    }
}
