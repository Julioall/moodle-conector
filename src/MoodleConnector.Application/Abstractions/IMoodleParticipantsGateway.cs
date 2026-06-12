using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleParticipantsGateway
{
    Task<CourseParticipantsPage> GetCourseParticipantsAsync(
        string userExternalId,
        string courseId,
        ParticipantStatusFilter statusFilter,
        int page,
        int pageSize,
        bool studentsOnly,
        bool includeEmail,
        string? groupId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken);
}
