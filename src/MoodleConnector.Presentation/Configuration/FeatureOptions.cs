namespace MoodleConnector.Presentation.Configuration;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool MessagesWriteEnabled { get; init; }
    public bool ScheduledMessagesEnabled { get; init; }
    public bool AssignmentFeedbackWriteEnabled { get; init; }
    public bool AssignmentGradeWriteEnabled { get; init; }
    public bool CourseContentWriteEnabled { get; init; }
    public bool DemoToolsEnabled { get; init; }
    public bool AppV2Enabled { get; init; }
}

