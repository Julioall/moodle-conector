using MoodleConnector.Domain;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public sealed class OperationRegistry : IOperationRegistry
{
    private readonly Dictionary<string, MoodleOperation> _operations;

    public OperationRegistry()
    {
        var seed = new[]
        {
            new MoodleOperation("core_course_get_courses", "course", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "course-list"),
            new MoodleOperation("core_enrol_get_users_courses", "course", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "course-list", ValidationStatus.LiveValidated),
            new MoodleOperation("core_course_get_courses_by_field", "course", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "course-list", ValidationStatus.LiveValidated),
            new MoodleOperation("mod_assign_get_submissions", "assignment", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "assignment-submissions", ValidationStatus.LiveValidated),
            new MoodleOperation("gradereport_user_get_grade_items", "gradebook", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "user-grades"),
            new MoodleOperation("core_completion_get_activities_completion_status", "completion", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "activity-completion")
        };

        _operations = seed.ToDictionary(o => o.OperationName, StringComparer.OrdinalIgnoreCase);
    }

    public MoodleOperation? GetOperation(string operationName)
    {
        return _operations.TryGetValue(operationName, out var op) ? op : null;
    }

    public IReadOnlyList<MoodleOperation> GetAllOperations()
    {
        return _operations.Values.ToList();
    }
}
