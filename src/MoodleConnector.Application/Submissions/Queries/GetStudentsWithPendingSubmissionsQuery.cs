using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Submissions.Queries;

/// <summary>
/// Resumo de uma atividade pendente (não entregue) por estudante.
/// </summary>
public sealed record PendingSubmissionItem(
    string AssignmentId,
    string AssignmentName,
    DateTimeOffset? DueDate,
    bool IsOverdue);

/// <summary>
/// Resumo de estudante com pelo menos uma SA pendente.
/// </summary>
public sealed record StudentPendingSubmissionSummary(
    string StudentId,
    string FullName,
    DateTimeOffset? LastCourseAccessAt,
    IReadOnlyList<PendingSubmissionItem> PendingAssignments);

public sealed record GetStudentsWithPendingSubmissionsResult(
    string CourseId,
    int TotalStudentsAnalyzed,
    int DueDaysAhead,
    IReadOnlyList<StudentPendingSubmissionSummary> Students,
    IReadOnlyList<string> SuggestedRecipientIds,
    string? Warning);

/// <summary>
/// Lista estudantes com SAs pendentes de entrega.
///
/// Estratégia:
/// 1. Busca todos os módulos do tipo 'assign' no curso.
/// 2. Para cada assign, obtém quem não submeteu (status: notsubmitted).
/// 3. Consolida por estudante.
///
/// DueDaysAhead = 0 → inclui todas as atividades sem entrega, independente do prazo.
/// DueDaysAhead > 0 → inclui apenas atividades cujo prazo está nos próximos N dias (ou já vencido).
/// </summary>
public sealed record GetStudentsWithPendingSubmissionsQuery(
    string CourseId,
    int DueDaysAhead = 0,
    int MaxStudentsToAnalyze = 100) : IRequest<GetStudentsWithPendingSubmissionsResult>;

public sealed class GetStudentsWithPendingSubmissionsQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GetStudentsWithPendingSubmissionsQuery, GetStudentsWithPendingSubmissionsResult>
{
    public async Task<GetStudentsWithPendingSubmissionsResult> Handle(
        GetStudentsWithPendingSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();

        // 1. Fetch active students
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

        var studentMap = participantsPage.Participants.ToDictionary(p => p.UserId, p => p);
        var pendingByStudent = studentMap.Keys.ToDictionary(id => id, _ => new List<PendingSubmissionItem>());

        // 2. Fetch course contents — only assign modules
        CourseContentsSummary contents;
        try
        {
            contents = await contentsGateway.GetCourseContentsAsync(
                userExternalId: currentUserExternalId,
                courseId: request.CourseId,
                moduleTypes: ["assign"],
                includeHidden: false,
                onlyWithFiles: false,
                cancellationToken: cancellationToken);
        }
        catch
        {
            return new GetStudentsWithPendingSubmissionsResult(
                CourseId: request.CourseId,
                TotalStudentsAnalyzed: 0,
                DueDaysAhead: request.DueDaysAhead,
                Students: [],
                SuggestedRecipientIds: [],
                Warning: "Não foi possível carregar os conteúdos do curso para identificar atividades.");
        }

        var now = DateTimeOffset.UtcNow;
        var assignmentsProcessed = 0;

        // 3. Iterate assign modules
        foreach (var section in contents.Sections)
        {
            foreach (var module in section.Modules.Where(m =>
                string.Equals(m.ModuleType, "assign", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(m.InstanceId)))
            {
                // Resolve DueDate from module dates (label matching "Due date" or similar)
                var dueDate = module.Dates
                    .FirstOrDefault(d =>
                        d.Label.Contains("due", StringComparison.OrdinalIgnoreCase) ||
                        d.Label.Contains("prazo", StringComparison.OrdinalIgnoreCase) ||
                        d.Label.Contains("entrega", StringComparison.OrdinalIgnoreCase))
                    ?.Date;

                // Filter by due date window if requested
                if (request.DueDaysAhead > 0 && dueDate.HasValue)
                {
                    var daysUntilDue = (dueDate.Value - now).TotalDays;
                    if (daysUntilDue > request.DueDaysAhead)
                    {
                        continue;
                    }
                }

                bool isOverdue = dueDate.HasValue && dueDate.Value < now;

                IReadOnlyList<AssignmentSubmissionRecord> notSubmitted;
                try
                {
                    notSubmitted = await submissionsGateway.GetAssignmentSubmissionsAsync(
                        userExternalId: currentUserExternalId,
                        assignmentId: module.InstanceId!,
                        status: "notsubmitted",
                        since: null,
                        before: null,
                        cancellationToken: cancellationToken);
                }
                catch
                {
                    continue;
                }

                assignmentsProcessed++;

                var pendingItem = new PendingSubmissionItem(
                    AssignmentId: module.InstanceId!,
                    AssignmentName: module.Name,
                    DueDate: dueDate,
                    IsOverdue: isOverdue);

                foreach (var record in notSubmitted)
                {
                    if (pendingByStudent.TryGetValue(record.UserId, out var pendingList))
                    {
                        pendingList.Add(pendingItem);
                    }
                }
            }
        }

        // 4. Build result
        var studentsWithPending = pendingByStudent
            .Where(kv => kv.Value.Count > 0)
            .Select(kv =>
            {
                var student = studentMap[kv.Key];
                return new StudentPendingSubmissionSummary(
                    StudentId: kv.Key,
                    FullName: student.FullName,
                    LastCourseAccessAt: student.LastCourseAccessAt,
                    PendingAssignments: kv.Value);
            })
            .OrderByDescending(s => s.PendingAssignments.Count)
            .ToList();

        string? warning = null;
        if (assignmentsProcessed == 0)
        {
            warning = "Nenhuma atividade do tipo 'assign' foi encontrada ou processada no curso. " +
                      "Verifique se o curso possui atividades avaliativas configuradas.";
        }

        var suggestedRecipients = studentsWithPending.Select(s => s.StudentId).ToList();

        return new GetStudentsWithPendingSubmissionsResult(
            CourseId: request.CourseId,
            TotalStudentsAnalyzed: participantsPage.Participants.Count,
            DueDaysAhead: request.DueDaysAhead,
            Students: studentsWithPending,
            SuggestedRecipientIds: suggestedRecipients,
            Warning: warning);
    }
}
