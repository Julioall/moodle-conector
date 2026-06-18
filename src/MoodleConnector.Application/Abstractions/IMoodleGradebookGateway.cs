namespace MoodleConnector.Application.Abstractions;

public record GradebookItem(
    string Id,
    string ItemName,
    string ItemType,
    string ItemModule,
    string? CategoryId,
    decimal? GradeRaw,
    string? GradeFormatted,
    decimal? GradeMin,
    decimal? GradeMax,
    decimal? PercentageFormatted,
    string? Feedback,
    string? FeedbackFormat,
    long? GradedDateSubmitted,
    long? GradedDateGraded,
    string? GraderId);

public record CourseGradebook(
    string CourseId,
    string StudentId,
    IReadOnlyCollection<GradebookItem> Items);

public interface IMoodleGradebookGateway
{
    Task<CourseGradebook> GetStudentGradebookAsync(
        string courseId,
        string studentId,
        CancellationToken cancellationToken);
}
