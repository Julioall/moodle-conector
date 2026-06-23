using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public sealed record AssignmentSettingsSummary(
    string AssignmentId,
    decimal MaxGrade,
    string? Name = null);

public interface IMoodleAssignmentSettingsGateway
{
    Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
        string userExternalId,
        string courseId,
        string assignmentId,
        CancellationToken cancellationToken);
}
