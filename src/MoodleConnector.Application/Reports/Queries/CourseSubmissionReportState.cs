using MediatR;
using MoodleConnector.Application.Submissions.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Reports.Queries;

/// <summary>
/// Authoritative delivery state for derived reports. Gradebook values alone do
/// not distinguish a missing submission from a submission awaiting grading.
/// </summary>
internal sealed record CourseSubmissionReportState(
    bool IsAvailable,
    bool IsComplete,
    IReadOnlyDictionary<string, IReadOnlyList<PendingSubmissionItem>> PendingByStudent,
    IReadOnlyDictionary<string, IReadOnlyList<AwaitingGradingSubmission>> AwaitingByStudent,
    IReadOnlyDictionary<string, IReadOnlyList<SubmissionEvaluationItem>> EvaluationsByStudent,
    string? Warning,
    IReadOnlyCollection<string> ActiveAssignmentIds)
{
    public static CourseSubmissionReportState Unavailable { get; } = new(
        false,
        false,
        new Dictionary<string, IReadOnlyList<PendingSubmissionItem>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<AwaitingGradingSubmission>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<SubmissionEvaluationItem>>(StringComparer.OrdinalIgnoreCase),
        "O estado de entrega não estava disponível; pendências não foram inferidas apenas pela ausência de nota.",
        []);

    public static async Task<CourseSubmissionReportState> LoadAsync(
        IMediator? mediator,
        string courseId,
        int maxStudentsToAnalyze,
        CancellationToken cancellationToken)
    {
        if (mediator is null)
        {
            return Unavailable;
        }

        try
        {
            var result = await mediator.Send(
                new GetStudentsWithPendingSubmissionsQuery(
                    courseId,
                    DueDaysAhead: 0,
                    MaxStudentsToAnalyze: maxStudentsToAnalyze,
                    IncludeAwaitingGrading: true,
                    ExcludeFutureActivities: true),
                cancellationToken);

            var activeAssignmentIds = result.Evaluations
                .Select(item => item.AssignmentId)
                .Concat(result.AwaitingGrading.Select(item => item.AssignmentId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new CourseSubmissionReportState(
                true,
                result.IsComplete,
                result.Students.ToDictionary(
                    item => item.StudentId,
                    item => (IReadOnlyList<PendingSubmissionItem>)item.PendingAssignments,
                    StringComparer.OrdinalIgnoreCase),
                result.AwaitingGrading
                    .GroupBy(item => item.StudentId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<AwaitingGradingSubmission>)group.ToArray(),
                        StringComparer.OrdinalIgnoreCase),
                result.Evaluations
                    .GroupBy(item => item.StudentId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<SubmissionEvaluationItem>)group.ToArray(),
                        StringComparer.OrdinalIgnoreCase),
                result.Warning,
                activeAssignmentIds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Unavailable;
        }
    }

    public IReadOnlyList<PendingSubmissionItem> PendingFor(string studentId) =>
        PendingByStudent.GetValueOrDefault(studentId) ?? [];

    public IReadOnlyList<AwaitingGradingSubmission> AwaitingFor(string studentId) =>
        AwaitingByStudent.GetValueOrDefault(studentId) ?? [];

    public int CountFor(string studentId, SubmissionEvaluationState state) =>
        (EvaluationsByStudent.GetValueOrDefault(studentId) ?? []).Count(item => item.State == state);

    public int ActiveAssignmentCount => ActiveAssignmentIds.Count;
}
