using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Tools.Completion;
using MoodleConnector.Presentation.Tools.Forums;
using MoodleConnector.Presentation.Tools.Gradebook;
using MoodleConnector.Presentation.Tools.Grading;
using MoodleConnector.Presentation.Tools.Memory;
using MoodleConnector.Presentation.Tools.Messages;
using MoodleConnector.Presentation.Tools.Monitor;
using MoodleConnector.Presentation.Tools.Pedagogy;
using MoodleConnector.Presentation.Tools.Reports;
using MoodleConnector.Presentation.Tools.Risk;
using MoodleConnector.Presentation.Tools.Submissions;
using MoodleConnector.Application.Configuration;
using ModelContextProtocol.Server;
using System.Reflection;

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
        typeof(MoodleWriteReconciliationTools),
        typeof(MoodleParticipantsTools),
        typeof(MoodleCourseContentsTools),
        typeof(MoodleCourseActivitiesTools),
        typeof(MoodleScormTools),
        typeof(MoodleForumTools),
        typeof(MoodleForumParticipationTools),
        typeof(MoodleAssignmentSubmissionsTools),
        typeof(MoodlePendingSubmissionsTools),
        typeof(MoodleGradingTools),
        typeof(MoodleGradebookTools),
        typeof(MoodleStudentPerformanceTools),
        typeof(MoodleAccessMonitoringTools),
        typeof(MoodleRiskAnalysisTools),
        typeof(MoodleReportTools),
        typeof(MoodleMonitorTools),
        typeof(MoodleMemoryTools),
        typeof(MoodleMemoryDocumentTools),
        typeof(MoodlePedagogyTools)
    ];

    public static IReadOnlyList<ConditionalMcpToolContainer> Conditional { get; } =
    [
        new(typeof(MoodleTutorMessageTools), "MessagesWriteEnabled"),
        new(typeof(MoodleUniversalWriteTools), "UniversalMoodleWriteEnabled"),
        new(typeof(MoodleDownloadFileTools), "UniversalMoodleFileDownloadEnabled")
    ];

    public static IReadOnlyList<Type> All { get; } =
        AlwaysOn.Concat(Conditional.Select(container => container.ContainerType)).ToArray();

    private static IReadOnlyDictionary<string, ConditionalMcpToolContainer> ConditionalByTool { get; } =
        Conditional
            .SelectMany(container => container.ContainerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Select(method => (container, methodName: method.Name, attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
                .Where(item => item.attribute is not null)
                .Select(item => (item.attribute!.Name ?? item.methodName, item.container)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
            .ToDictionary(item => item.Item1, item => item.container, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Type> GetEnabledContainers(
        FeatureOptions featureOptions,
        AssignmentWriteFeatureOptions assignmentWriteOptions)
    {
        return Conditional
            .Where(container => container.IsEnabled(featureOptions, assignmentWriteOptions))
            .Select(container => container.ContainerType)
            .ToArray();
    }

    public static bool IsToolEnabled(
        string toolName,
        FeatureOptions featureOptions,
        AssignmentWriteFeatureOptions assignmentWriteOptions)
    {
        return !ConditionalByTool.TryGetValue(toolName, out var container) ||
            container.IsEnabled(featureOptions, assignmentWriteOptions);
    }
}

public sealed record ConditionalMcpToolContainer(Type ContainerType, string FeatureFlag)
{
    public bool IsEnabled(FeatureOptions featureOptions, AssignmentWriteFeatureOptions assignmentWriteOptions) =>
        FeatureFlag switch
        {
            "AssignmentGradeWriteEnabled" => assignmentWriteOptions.AssignmentGradeWriteEnabled,
            "MessagesWriteEnabled" => featureOptions.MessagesWriteEnabled,
            "UniversalMoodleWriteEnabled" => featureOptions.UniversalMoodleWriteEnabled,
            "UniversalMoodleFileDownloadEnabled" => featureOptions.UniversalMoodleFileDownloadEnabled,
            _ => false
        };
}
