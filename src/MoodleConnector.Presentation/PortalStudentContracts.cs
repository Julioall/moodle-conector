using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Domain;

public sealed record PortalStudentRef(string ConnectionRef, string StudentId);
public sealed record PortalStudentCourseDto(
    string ConnectionRef, string CourseId, string Name, string? Url,
    string EnrollmentStatus, decimal? Progress, DateTimeOffset? LastCourseAccessAt,
    IReadOnlyList<PortalStudentGradeDto> Grades);
public sealed record PortalStudentGradeDto(
    string ItemId, string Name, decimal? Grade, decimal? Maximum,
    decimal? Percentage, string? Feedback, bool ReadOnly);
public sealed record PortalStudentDto(
    PortalStudentRef StudentRef, string ConnectionRef, string StudentId, string Name,
    string? Email, bool? Suspended, DateTimeOffset? FirstAccessAt,
    DateTimeOffset? LastAccessAt, DateTimeOffset? LastCourseAccessAt,
    string Risk, IReadOnlyList<string> RiskFactors, IReadOnlyList<PortalStudentCourseDto> Courses,
    string? MoodleUrl);

public static class PortalStudentContractMapper
{
    public static PortalStudentDto ToDto(
        string connectionRef,
        CourseParticipantSummary participant,
        IReadOnlyList<PortalStudentCourseDto>? courses = null,
        string? moodleUrl = null)
    {
        var factors = new List<string>();
        var risk = "normal";
        if (!participant.LastCourseAccessAt.HasValue)
        {
            risk = "risco";
            factors.Add("Sem acesso ao curso");
        }
        else if ((DateTimeOffset.UtcNow - participant.LastCourseAccessAt.Value).TotalDays >= 14)
        {
            risk = "atencao";
            factors.Add("Sem acesso ao curso há 14 dias ou mais");
        }

        return new(new(connectionRef, participant.UserId), connectionRef, participant.UserId,
            participant.FullName, participant.Email, participant.Suspended,
            participant.FirstAccessAt, participant.LastAccessAt, participant.LastCourseAccessAt,
            risk, factors, courses ?? Array.Empty<PortalStudentCourseDto>(), moodleUrl);
    }

    public static PortalStudentGradeDto ToGradeDto(StudentGradeItem item) =>
        new(item.ItemId, item.ItemName, item.GradeRaw, item.GradeMax,
            item.PercentageFormatted, item.Feedback, ReadOnly: true);
}
