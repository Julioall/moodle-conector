using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Gradebook.Queries;

/// <summary>
/// Resultado agregado de estudantes com conceito abaixo do mínimo em alguma atividade.
/// Inclui público-alvo sugerido para mensagem de recuperação.
/// </summary>
public sealed record StudentBelowMinimumSummary(
    string StudentId,
    string FullName,
    DateTimeOffset? LastCourseAccessAt,
    IReadOnlyList<StudentGradeItem> BelowMinimumItems);

public sealed record GetStudentsBelowMinGradeResult(
    string CourseId,
    decimal MinGradePercent,
    int TotalStudentsAnalyzed,
    IReadOnlyList<StudentBelowMinimumSummary> Students,
    IReadOnlyList<string> SuggestedRecipientIds,
    string? Warning);

public sealed record GetStudentsBelowMinGradeQuery(
    string CourseId,
    decimal MinGradePercent = 60m,
    int MaxStudentsToAnalyze = 100) : IRequest<GetStudentsBelowMinGradeResult>;

public sealed class GetStudentsBelowMinGradeQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleGradebookGateway gradebookGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GetStudentsBelowMinGradeQuery, GetStudentsBelowMinGradeResult>
{
    public async Task<GetStudentsBelowMinGradeResult> Handle(
        GetStudentsBelowMinGradeQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();

        var participantsPage = await participantsGateway.GetCourseParticipantsAsync(
            userExternalId: currentUserExternalId,
            courseId: request.CourseId,
            statusFilter: ParticipantStatusFilter.Active,
            page: 0,
            pageSize: request.MaxStudentsToAnalyze > 0 ? request.MaxStudentsToAnalyze : 100,
            studentsOnly: true,
            includeEmail: false,
            groupId: null,
            cancellationToken: cancellationToken);

        var results = new List<StudentBelowMinimumSummary>();
        var warnings = new List<string>();

        foreach (var student in participantsPage.Participants)
        {
            IReadOnlyList<StudentGradeItem> belowMinimumItems;
            try
            {
                var gradebook = await gradebookGateway.GetStudentGradebookAsync(
                    request.CourseId, student.UserId, cancellationToken);

                belowMinimumItems = gradebook.Items
                    .Where(GradebookMappingHelper.IsDerivedReportItem)
                    .Select(i => GradebookMappingHelper.ToStudentGradeItem(i, request.MinGradePercent))
                    .Where(i => i.BelowMinimum)
                    .ToList();
            }
            catch
            {
                // Gradebook may be unavailable for this student — skip silently
                continue;
            }

            if (belowMinimumItems.Count > 0)
            {
                results.Add(new StudentBelowMinimumSummary(
                    StudentId: student.UserId,
                    FullName: student.FullName,
                    LastCourseAccessAt: student.LastCourseAccessAt,
                    BelowMinimumItems: belowMinimumItems));
            }
        }

        string? warning = null;
        if (participantsPage.Participants.Count == 0)
        {
            warning = "Nenhum estudante ativo encontrado no curso.";
        }

        var suggestedRecipients = results.Select(r => r.StudentId).ToList();

        return new GetStudentsBelowMinGradeResult(
            CourseId: request.CourseId,
            MinGradePercent: request.MinGradePercent,
            TotalStudentsAnalyzed: participantsPage.Participants.Count,
            Students: results,
            SuggestedRecipientIds: suggestedRecipients,
            Warning: warning);
    }
}
