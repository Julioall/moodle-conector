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
    public bool IsComplete { get; init; } = true;
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
    int MaxAssignmentsToAnalyze = 0,
    CourseContentsSummary? PrefetchedContents = null,
    CourseParticipantsPage? PrefetchedParticipants = null) : IRequest<GetStudentsWithPendingSubmissionsResult>;

public sealed class GetStudentsWithPendingSubmissionsQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway,
    IMoodleAssignmentSettingsGateway assignmentSettingsGateway)
    : IRequestHandler<GetStudentsWithPendingSubmissionsQuery, GetStudentsWithPendingSubmissionsResult>
{
    public async Task<GetStudentsWithPendingSubmissionsResult> Handle(
        GetStudentsWithPendingSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();

        // 1. Fetch active students
        var participantsPage = IsForCourse(request.PrefetchedParticipants, request.CourseId)
            ? request.PrefetchedParticipants! with
            {
                Participants = request.PrefetchedParticipants!.Participants
                    .Take(request.MaxStudentsToAnalyze > 0 ? request.MaxStudentsToAnalyze : 100)
                    .ToArray()
            }
            : await participantsGateway.GetCourseParticipantsAsync(
                userExternalId: currentUserExternalId,
                courseId: request.CourseId,
                statusFilter: ParticipantStatusFilter.Active,
                page: 1,
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
            contents = IsForCourse(request.PrefetchedContents, request.CourseId)
                ? request.PrefetchedContents!
                : await contentsGateway.GetCourseContentsAsync(
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
                Warning: "Não foi possível carregar os conteúdos do curso para identificar atividades.")
            {
                IsComplete = false,
            };
        }

        var now = DateTimeOffset.UtcNow;
        var assignmentsProcessed = 0;

        // 3. Build the assignment scope first. The gateway then sends the
        // assignment IDs in batches instead of making one request per activity.
        var assignModules = contents.Sections
            .SelectMany(section => section.Modules)
            .Where(module =>
                string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(module.InstanceId))
            .ToList();
        var modulesToProcess = request.MaxAssignmentsToAnalyze > 0
            ? assignModules.Take(request.MaxAssignmentsToAnalyze)
            : assignModules;

        var assignmentContexts = modulesToProcess
            .Select(module => new AssignmentContext(
                module,
                module.Dates
                    .FirstOrDefault(d =>
                        d.Label.Contains("due", StringComparison.OrdinalIgnoreCase) ||
                        d.Label.Contains("prazo", StringComparison.OrdinalIgnoreCase) ||
                        d.Label.Contains("entrega", StringComparison.OrdinalIgnoreCase))
                    ?.Date))
            .Where(context => request.DueDaysAhead <= 0 ||
                !context.DueDate.HasValue ||
                (context.DueDate.Value - now).TotalDays <= request.DueDaysAhead)
            .ToArray();

        // Uma atividade sem nota ainda pode exigir feedback. O grau da
        // atividade só define qual sinal devemos usar para saber se ela foi
        // tratada; não é motivo para removê-la da fila do tutor.
        IReadOnlyDictionary<string, AssignmentSettingsSummary> assignmentSettings =
            new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal);
        if (assignmentContexts.Length > 0)
        {
            try
            {
                assignmentSettings = await assignmentSettingsGateway.GetCourseAssignmentSettingsAsync(
                    currentUserExternalId,
                    request.CourseId,
                    cancellationToken);
            }
            catch
            {
                // Mantém o comportamento baseado em gradingstatus se a leitura
                // das configurações não estiver disponível.
            }
        }

        var canClassifyNoGradeActivities = assignmentSettings.Count > 0;
        // Atividades sem nota exigiriam uma leitura Moodle por envio para
        // confirmar feedback. Elas ficam fora do contador agregado para que a
        // atualização diária permaneça previsível mesmo em turmas grandes.
        var noGradeFeedbackSkipped = false;
        var submissionReadFailed = false;

        IReadOnlyList<AssignmentSubmissionsBatch> submissionBatches = [];
        var submissionFailures = new List<string>();
        if (assignmentContexts.Length > 0)
        {
            try
            {
                submissionBatches = await submissionsGateway.GetAssignmentSubmissionsBatchAsync(
                    currentUserExternalId,
                    assignmentContexts.Select(context => context.Module.InstanceId!).ToArray(),
                    request.IncludeAwaitingGrading ? null : "notsubmitted",
                    since: null,
                    before: null,
                    cancellationToken);
            }
            catch
            {
                submissionBatches = [];
                submissionReadFailed = true;
            }
        }

        var contextsByAssignmentId = assignmentContexts
            .ToDictionary(context => context.Module.InstanceId!, StringComparer.Ordinal);

        foreach (var batch in submissionBatches)
        {
            if (!string.IsNullOrWhiteSpace(batch.ErrorCode))
            {
                submissionFailures.Add(
                    $"{batch.AssignmentId} ({batch.ErrorCode})");
                continue;
            }

            if (!contextsByAssignmentId.TryGetValue(batch.AssignmentId, out var context))
            {
                continue;
            }

            assignmentsProcessed++;
            var module = context.Module;
            var dueDate = context.DueDate;
            var isOverdue = dueDate.HasValue && dueDate.Value < now;
            var pendingItem = new PendingSubmissionItem(
                AssignmentId: module.InstanceId!,
                AssignmentName: module.Name,
                DueDate: dueDate,
                IsOverdue: isOverdue);
            var isNoGradeActivity = canClassifyNoGradeActivities &&
                IsNoGradeActivity(module, assignmentSettings);
            foreach (var record in batch.Submissions)
            {
                if (string.Equals(record.Status, "notsubmitted", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.Status, "not_submitted", StringComparison.OrdinalIgnoreCase))
                {
                    // Atividades extras só entram na fila de feedback depois
                    // que houve envio; não são cobranças de entrega avaliativa.
                    if (!isNoGradeActivity && pendingByStudent.TryGetValue(record.UserId, out var pendingList))
                        pendingList.Add(pendingItem);
                }
                else if (request.IncludeAwaitingGrading &&
                         string.Equals(record.Status, "submitted", StringComparison.OrdinalIgnoreCase))
                {
                    var needsFeedback = IsAwaitingGrading(record.GradingStatus);
                    if (isNoGradeActivity)
                    {
                        noGradeFeedbackSkipped = true;
                        continue;
                    }

                    if (needsFeedback && gradingByStudent.TryGetValue(record.UserId, out var gradingList))
                    {
                        gradingList.Add(new AwaitingGradingItem(
                            module.InstanceId!,
                            module.Name,
                            dueDate,
                            record.ModifiedAt ?? record.CreatedAt));
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
        if (assignModules.Count == 0)
        {
            warning = "Nenhuma atividade do tipo 'assign' foi encontrada no curso. " +
                      "Verifique se há atividades avaliativas configuradas.";
        }
        else if (assignmentContexts.Length == 0)
        {
            warning = "As atividades do tipo 'assign' encontradas ficaram fora do período solicitado para esta consulta.";
        }
        else if (assignmentsProcessed == 0)
        {
            warning = "Foram encontradas atividades do tipo 'assign', mas nenhuma foi processada. " +
                      "Verifique os erros de leitura retornados pelo Moodle.";
        }
        else if (request.MaxAssignmentsToAnalyze > 0 && assignModules.Count > request.MaxAssignmentsToAnalyze)
        {
            warning = $"A análise foi limitada às {request.MaxAssignmentsToAnalyze} primeiras atividades avaliativas para preservar o desempenho.";
        }

        if (noGradeFeedbackSkipped)
        {
            warning = string.IsNullOrWhiteSpace(warning)
                ? "Atividades sem nota foram omitidas do contador de correções para evitar consultas individuais por envio."
                : $"{warning} Atividades sem nota foram omitidas do contador de correções para evitar consultas individuais por envio.";
        }

        if (submissionFailures.Count > 0)
        {
            var submissionWarning = "Não foi possível ler as submissões de " +
                $"{submissionFailures.Count} atividade(s): {string.Join(", ", submissionFailures)}. " +
                "As demais atividades foram processadas normalmente.";
            warning = string.IsNullOrWhiteSpace(warning)
                ? submissionWarning
                : $"{warning} {submissionWarning}";
        }

        if (submissionReadFailed)
        {
            const string submissionWarning = "Não foi possível ler as submissões das atividades avaliativas; a contagem deste curso está incompleta.";
            warning = string.IsNullOrWhiteSpace(warning)
                ? submissionWarning
                : $"{warning} {submissionWarning}";
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
            IsComplete = !submissionReadFailed && submissionFailures.Count == 0,
        };
    }

    private static bool IsAwaitingGrading(string? gradingStatus) =>
        string.Equals(gradingStatus, "notgraded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(gradingStatus, "needsgrading", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(gradingStatus, "notmarked", StringComparison.OrdinalIgnoreCase);

    private static bool IsForCourse(CourseContentsSummary? contents, string courseId) =>
        contents is not null &&
        string.Equals(contents.CourseId, courseId, StringComparison.OrdinalIgnoreCase);

    private static bool IsForCourse(CourseParticipantsPage? participants, string courseId) =>
        participants is not null &&
        string.Equals(participants.CourseId, courseId, StringComparison.OrdinalIgnoreCase);

    private static bool IsNoGradeActivity(
        CourseModuleSummary module,
        IReadOnlyDictionary<string, AssignmentSettingsSummary> settings)
    {
        if (settings.TryGetValue(module.InstanceId ?? string.Empty, out var byInstance))
        {
            return byInstance.MaxGrade <= 0;
        }

        return settings.TryGetValue(module.ModuleId ?? string.Empty, out var byModule) &&
            byModule.MaxGrade <= 0;
    }

    private sealed record AssignmentContext(CourseModuleSummary Module, DateTimeOffset? DueDate);
}
