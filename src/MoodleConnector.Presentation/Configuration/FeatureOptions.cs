namespace MoodleConnector.Presentation.Configuration;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool MessagesWriteEnabled { get; init; }
    public bool ScheduledMessagesEnabled { get; init; }
    public bool AssignmentFeedbackWriteEnabled { get; init; }
    public bool AssignmentGradeWriteEnabled { get; init; }
    public bool CourseContentWriteEnabled { get; init; }
    public bool UniversalMoodleWriteEnabled { get; init; }
    public bool UniversalMoodleFileDownloadEnabled { get; init; }
    public bool AppV2Enabled { get; init; }
    /// <summary>Controls rollout of the professional agenda routes and MCP surface.</summary>
    public bool ProfessionalAgendaEnabled { get; init; }
    /// <summary>Controls rollout of the professional Tasks routes and MCP surface.</summary>
    public bool ProfessionalTasksEnabled { get; init; }
}

