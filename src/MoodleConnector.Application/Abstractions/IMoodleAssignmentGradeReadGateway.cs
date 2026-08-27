using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleAssignmentGradeReadGateway
{
    async Task<IReadOnlyDictionary<string, AssignmentExistingGrade>> GetExistingGradesAsync(
        string userExternalId,
        string assignmentId,
        IReadOnlyCollection<string> studentIds,
        CancellationToken cancellationToken)
    {
        var grades = new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase);
        foreach (var studentId in studentIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var grade = await GetExistingGradeAsync(userExternalId, assignmentId, studentId, cancellationToken);
            if (grade is not null)
            {
                grades[studentId] = grade;
            }
        }

        return grades;
    }

    Task<AssignmentExistingGrade?> GetExistingGradeAsync(
        string userExternalId,
        string assignmentId,
        string studentId,
        CancellationToken cancellationToken);
}
