using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Reports.Queries;

public sealed record CourseGradeReportStudentRow(
    string StudentId,
    string FullName,
    DateTimeOffset? LastAccessAt,
    decimal? TotalGrade,
    decimal? TotalGradeMax,
    decimal? TotalGradePercentage,
    string? TotalGradeFormatted,
    string Status);

public sealed record GenerateCourseGradesReportResult(
    string CourseId,
    DateTimeOffset GeneratedAt,
    int TotalStudents,
    int StudentsWithGrade,
    int StudentsWithoutGrade,
    decimal? AveragePercentage,
    IReadOnlyList<CourseGradeReportStudentRow> Students,
    string? Warning);

public sealed record GenerateCourseGradesReportQuery(
    string CourseId,
    int PageSize = 100) : IRequest<GenerateCourseGradesReportResult>;

/// <summary>
/// Gera o relatório de notas usando somente o item total do curso retornado pelo Moodle.
/// O item total tem itemtype = "course"; notas de atividades não são somadas localmente.
/// </summary>
public sealed class GenerateCourseGradesReportQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleGradebookGateway gradebookGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GenerateCourseGradesReportQuery, GenerateCourseGradesReportResult>
{
    public async Task<GenerateCourseGradesReportResult> Handle(
        GenerateCourseGradesReportQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();
        var now = DateTimeOffset.UtcNow;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var participants = new List<CourseParticipantSummary>();
        var page = 1;

        while (true)
        {
            var participantsPage = await participantsGateway.GetCourseParticipantsAsync(
                userExternalId: currentUserExternalId,
                courseId: request.CourseId,
                statusFilter: ParticipantStatusFilter.Active,
                page: page,
                pageSize: pageSize,
                studentsOnly: true,
                includeEmail: false,
                groupId: null,
                cancellationToken: cancellationToken);

            participants.AddRange(participantsPage.Participants);
            if (!participantsPage.HasMore || participantsPage.Participants.Count == 0)
            {
                break;
            }

            page++;
        }

        if (participants.Count == 0)
        {
            return new GenerateCourseGradesReportResult(
                CourseId: request.CourseId,
                GeneratedAt: now,
                TotalStudents: 0,
                StudentsWithGrade: 0,
                StudentsWithoutGrade: 0,
                AveragePercentage: null,
                Students: [],
                Warning: "Nenhum estudante ativo encontrado no curso.");
        }

        var rows = new List<CourseGradeReportStudentRow>(participants.Count);
        foreach (var participant in participants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var gradebook = await gradebookGateway.GetStudentGradebookAsync(
                    request.CourseId,
                    participant.UserId,
                    cancellationToken);

                var courseTotal = gradebook.Items.FirstOrDefault(IsCourseTotalItem);
                rows.Add(courseTotal is null
                    ? WithoutGrade(participant)
                    : ToRow(participant, courseTotal));
            }
            catch
            {
                rows.Add(WithoutGrade(participant));
            }
        }

        var orderedRows = rows
            .OrderBy(row => row.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var percentages = orderedRows
            .Where(row => row.TotalGradePercentage.HasValue)
            .Select(row => row.TotalGradePercentage!.Value)
            .ToArray();

        return new GenerateCourseGradesReportResult(
            CourseId: request.CourseId,
            GeneratedAt: now,
            TotalStudents: orderedRows.Count,
            StudentsWithGrade: orderedRows.Count(row => row.TotalGrade.HasValue),
            StudentsWithoutGrade: orderedRows.Count(row => !row.TotalGrade.HasValue),
            AveragePercentage: percentages.Length == 0 ? null : percentages.Average(),
            Students: orderedRows,
            Warning: "A métrica usa a nota total do curso retornada pelo Moodle. Notas de atividades não são somadas localmente.");
    }

    private static bool IsCourseTotalItem(GradebookItem item) =>
        string.Equals(item.ItemType, "course", StringComparison.OrdinalIgnoreCase);

    private static CourseGradeReportStudentRow ToRow(
        CourseParticipantSummary participant,
        GradebookItem item) => new(
            StudentId: participant.UserId,
            FullName: participant.FullName,
            LastAccessAt: participant.LastAccessAt,
            TotalGrade: item.GradeRaw,
            TotalGradeMax: GradebookMappingHelper.ResolveGradeMax(item),
            TotalGradePercentage: GradebookMappingHelper.ResolvePercentage(item),
            TotalGradeFormatted: item.GradeFormatted,
            Status: item.GradeRaw.HasValue ? "com_nota" : "sem_nota");

    private static CourseGradeReportStudentRow WithoutGrade(CourseParticipantSummary participant) => new(
        StudentId: participant.UserId,
        FullName: participant.FullName,
        LastAccessAt: participant.LastAccessAt,
        TotalGrade: null,
        TotalGradeMax: null,
        TotalGradePercentage: null,
        TotalGradeFormatted: null,
        Status: "sem_nota");
}
