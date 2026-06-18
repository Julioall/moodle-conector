namespace MoodleConnector.Application.Abstractions;

public record ActivityCompletionStatus(
    string Cmid,
    string Modname,
    string Instance,
    long State,
    long Timecompleted,
    long Tracking,
    string? Overrideby,
    bool Valueused);

public record CourseCompletionStatus(
    bool Completed,
    long Timecompleted,
    IReadOnlyCollection<ActivityCompletionStatus> Activities);

public interface IMoodleCompletionGateway
{
    Task<CourseCompletionStatus> GetStudentCompletionAsync(
        string courseId,
        string studentId,
        CancellationToken cancellationToken);
}
