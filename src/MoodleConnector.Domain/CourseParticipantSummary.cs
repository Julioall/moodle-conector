namespace MoodleConnector.Domain;

public enum ParticipantStatusFilter
{
    Active = 0,
    Suspended = 1,
    All = 2
}

public enum ParticipantClassificationMode
{
    NotRequested = 0,
    RoleBased = 1,
    Mixed = 2,
    Fallback = 3
}

public sealed record ParticipantClassificationDiagnostics(
    int EvaluatedCount,
    int IncludedByStudentRoleCount,
    int IncludedByFallbackCount,
    int ExcludedKnownStaffCount,
    bool HasEmptyRoles,
    bool HasEmptyGroups,
    ParticipantClassificationMode Mode)
{
    public static ParticipantClassificationDiagnostics Empty { get; } =
        new(0, 0, 0, 0, false, false, ParticipantClassificationMode.NotRequested);
}

public sealed record CourseParticipantsPage(
    string CourseId,
    int Page,
    int PageSize,
    ParticipantStatusFilter StatusFilter,
    bool StudentsOnly,
    bool IncludeEmail,
    bool HasMore,
    IReadOnlyList<CourseParticipantSummary> Participants,
    ParticipantClassificationDiagnostics? ClassificationDiagnostics = null);

public sealed record CourseParticipantSummary(
    string UserId,
    string FullName,
    string? Email,
    bool? Suspended,
    DateTimeOffset? FirstAccessAt,
    DateTimeOffset? LastAccessAt,
    DateTimeOffset? LastCourseAccessAt,
    IReadOnlyList<CourseParticipantRole> Roles,
    IReadOnlyList<CourseParticipantGroup> Groups,
    string EnrollmentStatus = "unknown");

public sealed record CourseParticipantRole(
    string RoleId,
    string? ShortName,
    string Name);

public sealed record CourseParticipantGroup(
    string GroupId,
    string Name);
