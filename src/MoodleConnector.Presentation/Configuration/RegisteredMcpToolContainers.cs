using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Tools.Completion;
using MoodleConnector.Presentation.Tools.Forums;
using MoodleConnector.Presentation.Tools.Gradebook;
using MoodleConnector.Presentation.Tools.Grading;
using MoodleConnector.Presentation.Tools.Memory;
using MoodleConnector.Presentation.Tools.Messages;
using MoodleConnector.Presentation.Tools.Monitor;
using MoodleConnector.Presentation.Tools.Pedagogy;
using MoodleConnector.Presentation.Tools.Portal;
using MoodleConnector.Presentation.Tools.Reports;
using MoodleConnector.Presentation.Tools.Risk;
using MoodleConnector.Presentation.Tools.Submissions;
using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Presentation.Configuration;

/// <summary>
/// Single source of truth for tool containers whose metadata belongs in the
/// registry. Conditional tools are included so the inventory can describe the
/// complete catalog even when a feature flag keeps them hidden at runtime.
/// </summary>
public static class RegisteredMcpToolContainers
{
    public static IReadOnlyList<Type> AlwaysOn { get; } =
    [
        typeof(MoodleCoursesTools),
        typeof(MoodleUniversalTools),
        typeof(MoodleUniversalWriteTools),
        typeof(MoodleParticipantsTools),
        typeof(MoodleCourseContentsTools),
        typeof(MoodleCourseActivitiesTools),
        typeof(MoodleForumTools),
        typeof(MoodleForumParticipationTools),
        typeof(MoodleAssignmentSubmissionsTools),
        typeof(MoodlePendingSubmissionsTools),
        typeof(MoodleGradingTools),
        typeof(MoodleGradebookTools),
        typeof(MoodleStudentPerformanceTools),
        typeof(MoodleCompletionTools),
        typeof(MoodleAccessMonitoringTools),
        typeof(MoodleRiskAnalysisTools),
        typeof(MoodleGradingContextDiagnosticsTools),
        typeof(MoodleGradingReviewAppTools),
        typeof(MoodleTutorMessageTools),
        typeof(MoodleReportTools),
        typeof(MoodleMonitorTools),
        typeof(MoodleMemoryTools),
        typeof(MoodleMemoryDocumentTools),
        typeof(MoodlePedagogyTools),
        typeof(PortalTaskTools),
        typeof(PortalAgendaTools)
    ];

    public static IReadOnlyList<ConditionalMcpToolContainer> Conditional { get; } =
    [
        new(typeof(DemoPendingActionTools), "DemoToolsEnabled"),
        new(typeof(MoodleIndividualGradeTools), "AssignmentGradeWriteEnabled")
    ];

    public static IReadOnlyList<Type> All { get; } =
        AlwaysOn.Concat(Conditional.Select(container => container.ContainerType)).ToArray();

    public static IReadOnlyList<Type> GetEnabledContainers(
        FeatureOptions featureOptions,
        AssignmentWriteFeatureOptions assignmentWriteOptions)
    {
        return Conditional
            .Where(container => container.IsEnabled(featureOptions, assignmentWriteOptions))
            .Select(container => container.ContainerType)
            .ToArray();
    }
}

public sealed record ConditionalMcpToolContainer(Type ContainerType, string FeatureFlag)
{
    public bool IsEnabled(FeatureOptions featureOptions, AssignmentWriteFeatureOptions assignmentWriteOptions) =>
        FeatureFlag switch
        {
            "DemoToolsEnabled" => featureOptions.DemoToolsEnabled,
            "AssignmentGradeWriteEnabled" => assignmentWriteOptions.AssignmentGradeWriteEnabled,
            _ => false
        };
}
