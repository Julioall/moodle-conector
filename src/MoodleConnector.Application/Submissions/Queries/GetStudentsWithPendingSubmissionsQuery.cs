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

public sealed record AwaitingGradingItem(
    string AssignmentId,
    string AssignmentName,
    DateTimeOffset? DueDate,
    DateTimeOffset? SubmittedAt);

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
    string? Warning)
{
    public IReadOnlyList<(string StudentId, string FullName, DateTimeOffset? LastCourseAccessAt, AwaitingGradingItem Item)> AwaitingGrading { get; init; } = [];
}

/// <summary>
/// Lista estudantes com SAs pendentes de entrega.
///
/// Estratégia:
/// 1. Busca todos os módulos do tipo 'assign' no curso.
/// 2. Para cada assign, obtém quem não submeteu e, quando solicitado, quem
///    entregou e ainda aguarda correção.
/// 3. Consolida por estudante.
///
/// DueDaysAhead = 0 → inclui todas as atividades sem entrega, independente do prazo.
/// DueDaysAhead > 0 → inclui apenas atividades cujo prazo está nos próximos N dias (ou já vencido).
/// </summary>
public sealed record GetStudentsWithPendingSubmissionsQuery(
    string CourseId,
    int DueDaysAhead = 0,
    int MaxStudentsToAnalyze = 100,
    bool IncludeAwaitingGrading = false,
    int MaxAssignmentsToAnalyze = 0) : IRequest<GetStudentsWithPendingSubmissionsResult>;

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
        var gradingByStudent = studentMap.Keys.ToDictionary(id => id, _ => new List<AwaitingGradingItem>());

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

        // 3. Iterate assign modules. Dashboard callers can cap this because
        // each assignment requires one Moodle submissions request.
        var assignModules = contents.Sections
            .SelectMany(section => section.Modules)
            .Where(module =>
                string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(module.InstanceId))
            .ToList();
        var modulesToProcess = request.MaxAssignmentsToAnalyze > 0
            ? assignModules.Take(request.MaxAssignmentsToAnalyze)
            : assignModules;

        foreach (var module in modulesToProcess)
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

                IReadOnlyList<AssignmentSubmissionRecord> submissions;
                try
                {
                    submissions = await submissionsGateway.GetAssignmentSubmissionsAsync(
                        userExternalId: currentUserExternalId,
                        assignmentId: module.InstanceId!,
                        status: request.IncludeAwaitingGrading ? null : "notsubmitted",
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

                foreach (var record in submissions)
                {
                    if (string.Equals(record.Status, "notsubmitted", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.Status, "not_submitted", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pendingByStudent.TryGetValue(record.UserId, out var pendingList))
                            pendingList.Add(pendingItem);
                    }
                    else if (request.IncludeAwaitingGrading &&
                             string.Equals(record.Status, "submitted", StringComparison.OrdinalIgnoreCase) &&
                             IsAwaitingGrading(record.GradingStatus) &&
                             gradingByStudent.TryGetValue(record.UserId, out var gradingList))
                    {
                        gradingList.Add(new AwaitingGradingItem(module.InstanceId!, module.Name, dueDate, record.ModifiedAt ?? record.CreatedAt));
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
        else if (request.MaxAssignmentsToAnalyze > 0 && assignModules.Count > request.MaxAssignmentsToAnalyze)
        {
            warning = $"A análise foi limitada às {request.MaxAssignmentsToAnalyze} primeiras atividades avaliativas para preservar o desempenho.";
        }

        var suggestedRecipients = studentsWithPending.Select(s => s.StudentId).ToList();
        var awaitingGrading = gradingByStudent
            .Where(kv => kv.Value.Count > 0 && studentMap.ContainsKey(kv.Key))
            .SelectMany(kv => kv.Value.Select(item =>
            {
                var student = studentMap[kv.Key];
                return (kv.Key, student.FullName, student.LastCourseAccessAt, item);
            }))
            .ToArray();

        return new GetStudentsWithPendingSubmissionsResult(
            CourseId: request.CourseId,
            TotalStudentsAnalyzed: participantsPage.Participants.Count,
            DueDaysAhead: request.DueDaysAhead,
            Students: studentsWithPending,
            SuggestedRecipientIds: suggestedRecipients,
            Warning: warning)
        {
            AwaitingGrading = awaitingGrading,
        };
    }

    private static bool IsAwaitingGrading(string? gradingStatus) =>
        string.IsNullOrWhiteSpace(gradingStatus) ||
        string.Equals(gradingStatus, "notgraded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gradingStatus, "needsgrading", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gradingStatus, "notmarked", StringComparison.OrdinalIgnoreCase);
}
