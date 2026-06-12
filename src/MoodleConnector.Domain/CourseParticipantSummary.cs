namespace MoodleConnector.Domain;

public enum ParticipantStatusFilter
{
    Active = 0,
    Suspended = 1,
    All = 2
}

public sealed record CourseParticipantsPage(
    string CourseId,
    int Page,
    int PageSize,
    ParticipantStatusFilter StatusFilter,
    bool StudentsOnly,
    bool IncludeEmail,
    bool HasMore,
    IReadOnlyList<CourseParticipantSummary> Participants);

public sealed record CourseParticipantSummary(
    string UserId,
    string FullName,
    string? Email,
    bool? Suspended,
    DateTimeOffset? FirstAccessAt,
    DateTimeOffset? LastAccessAt,
    DateTimeOffset? LastCourseAccessAt,
    IReadOnlyList<CourseParticipantRole> Roles,
    IReadOnlyList<CourseParticipantGroup> Groups);

public sealed record CourseParticipantRole(
    string RoleId,
    string? ShortName,
    string Name);

public sealed record CourseParticipantGroup(
    string GroupId,
    string Name);
