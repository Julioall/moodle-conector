using MoodleConnector.Domain;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public sealed class OperationRegistry : IOperationRegistry
{
    private readonly Dictionary<string, MoodleOperation> _operations;

    public OperationRegistry()
    {
        var seed = new Dictionary<string, MoodleOperation>(StringComparer.OrdinalIgnoreCase)
        {
            ["core_webservice_get_site_info"] = new("core_webservice_get_site_info", "connection", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "site-info", ValidationStatus.LiveValidated),
            ["core_course_get_courses"] = new("core_course_get_courses", "course", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "course-list"),
            ["core_enrol_get_users_courses"] = new("core_enrol_get_users_courses", "course", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "course-list", ValidationStatus.LiveValidated),
            ["core_course_get_courses_by_field"] = new("core_course_get_courses_by_field", "course", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "course-list", ValidationStatus.LiveValidated),
            ["mod_assign_get_submissions"] = new("mod_assign_get_submissions", "assignment", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "assignment-submissions", ValidationStatus.LiveValidated),
            ["gradereport_user_get_grade_items"] = new("gradereport_user_get_grade_items", "gradebook", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "user-grades"),
            ["core_completion_get_activities_completion_status"] = new("core_completion_get_activities_completion_status", "completion", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "activity-completion")
        };

        foreach (var functionName in MoodleReadFunctionPolicy.KnownReadFunctions)
        {
            seed.TryAdd(
                functionName,
                new MoodleOperation(
                    functionName,
                    GetCategory(functionName),
                    OperationType.Read,
                    ToolRiskLevel.ReadOnly,
                    OperationPolicy.Direct,
                    GetNormalizationProfile(functionName)));
        }

        foreach (var functionName in MoodleReadFunctionPolicy.KnownControlledWriteFunctions)
        {
            seed.TryAdd(
                functionName,
                new MoodleOperation(
                    functionName,
                    GetCategory(functionName),
                    OperationType.ControlledWrite,
                    ToolRiskLevel.HumanConfirmedWrite,
                    OperationPolicy.Aggregated,
                    "controlled-write"));
        }

        _operations = seed;
    }

    public MoodleOperation? GetOperation(string operationName)
    {
        return _operations.TryGetValue(operationName, out var op) ? op : null;
    }

    public IReadOnlyList<MoodleOperation> GetAllOperations()
    {
        return _operations.Values.ToList();
    }

    private static string GetCategory(string functionName) =>
        functionName switch
        {
            _ when functionName.StartsWith("core_course_", StringComparison.OrdinalIgnoreCase) => "course",
            _ when functionName.StartsWith("core_enrol_", StringComparison.OrdinalIgnoreCase) => "enrollment",
            _ when functionName.StartsWith("core_user_", StringComparison.OrdinalIgnoreCase) => "student",
            _ when functionName.StartsWith("core_group_", StringComparison.OrdinalIgnoreCase) => "participants",
            _ when functionName.StartsWith("core_completion_", StringComparison.OrdinalIgnoreCase) => "completion",
            _ when functionName.StartsWith("gradereport_", StringComparison.OrdinalIgnoreCase) || functionName.StartsWith("core_grades_", StringComparison.OrdinalIgnoreCase) => "gradebook",
            _ when functionName.StartsWith("mod_assign_", StringComparison.OrdinalIgnoreCase) => "assignment",
            _ when functionName.StartsWith("mod_forum_", StringComparison.OrdinalIgnoreCase) => "forum",
            _ when functionName.StartsWith("core_message_", StringComparison.OrdinalIgnoreCase) || functionName.StartsWith("message_", StringComparison.OrdinalIgnoreCase) => "messaging",
            _ when functionName.StartsWith("core_calendar_", StringComparison.OrdinalIgnoreCase) => "calendar",
            _ when functionName.StartsWith("core_files_", StringComparison.OrdinalIgnoreCase) => "files",
            _ => "moodle"
        };

    private static string GetNormalizationProfile(string functionName) =>
        functionName switch
        {
            _ when functionName.Contains("course", StringComparison.OrdinalIgnoreCase) => "course-list",
            _ when functionName.Contains("submission", StringComparison.OrdinalIgnoreCase) => "assignment-submissions",
            _ when functionName.Contains("grade", StringComparison.OrdinalIgnoreCase) => "gradebook",
            _ when functionName.Contains("completion", StringComparison.OrdinalIgnoreCase) => "activity-completion",
            _ => "generic-read"
        };
}
