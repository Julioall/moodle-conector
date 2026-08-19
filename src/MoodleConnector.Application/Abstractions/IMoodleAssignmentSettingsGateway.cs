using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public sealed record AssignmentSettingsSummary(
    string AssignmentId,
    decimal MaxGrade,
    string? Name = null);

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
