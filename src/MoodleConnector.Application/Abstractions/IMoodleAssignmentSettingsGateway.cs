using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public sealed record AssignmentSettingsSummary(
    string AssignmentId,
    decimal MaxGrade,
    string? Name = null,
    // Null means that Moodle did not expose enough information to classify
    // the grading mode. A negative Moodle grade represents a scale and is
    // therefore gradable even though MaxGrade is intentionally kept at zero.
    bool? IsGradable = null);

public interface IMoodleAssignmentSettingsGateway
{
    Task<IReadOnlyDictionary<string, AssignmentSettingsSummary>> GetCourseAssignmentSettingsAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, AssignmentSettingsSummary>>(
            new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal));

    Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
        string userExternalId,
        string courseId,
        string assignmentId,
        CancellationToken cancellationToken);
}
