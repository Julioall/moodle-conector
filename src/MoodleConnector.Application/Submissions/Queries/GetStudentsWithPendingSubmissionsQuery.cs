using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
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
/// A serializable, self-contained record for a delivered assignment that is
/// awaiting grading. Value tuples serialize as empty objects in System.Text.Json.
/// </summary>
public sealed record AwaitingGradingSubmission(
    string CourseId,
    string AssignmentId,
    string AssignmentName,
    string StudentId,
    string StudentName,
    DateTimeOffset? LastCourseAccessAt,
    string SubmissionStatus,
    DateTimeOffset? SubmittedAt,
    string GradingStatus);

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
    public IReadOnlyList<AwaitingGradingSubmission> AwaitingGrading { get; init; } = [];
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
    CourseParticipantsPage? PrefetchedParticipants = null,
    CourseAssignmentSubmissionsSnapshot? PrefetchedSubmissions = null) : IRequest<GetStudentsWithPendingSubmissionsResult>;

public sealed class GetStudentsWithPendingSubmissionsQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway,
    IMoodleAssignmentSettingsGateway assignmentSettingsGateway,
    IMoodleAssignmentGradeReadGateway? gradeReadGateway = null)
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
        var assignmentSettings = GetSettingsFromSnapshot(
            request.PrefetchedSubmissions,
            request.CourseId,
            assignmentContexts);
        if (assignmentContexts.Length > 0 && assignmentSettings is null)
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

        assignmentSettings ??= new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal);

        var canClassifyNoGradeActivities = assignmentSettings.Count > 0;
        // Atividades sem nota usam feedback por atividade (não por envio).
        // Quando a leitura não está disponível, não inventamos pendências:
        // marcamos a cobertura como incompleta e preservamos a velocidade.
        var noGradeFeedbackSkipped = false;
        var feedbackByAssignment = new Dictionary<string, IReadOnlyDictionary<string, AssignmentExistingGrade>>(StringComparer.OrdinalIgnoreCase);
        var feedbackReadyAssignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var feedbackReadFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var submissionReadFailed = false;

        IReadOnlyList<AssignmentSubmissionsBatch> submissionBatches = [];
        var submissionFailures = new List<string>();
        if (assignmentContexts.Length > 0)
        {
            if (IsForCourse(request.PrefetchedSubmissions, request.CourseId))
            {
                submissionBatches = request.PrefetchedSubmissions!.Assignments
                    .Where(item => assignmentContexts.Any(context =>
                        string.Equals(context.Module.InstanceId, item.AssignmentId, StringComparison.OrdinalIgnoreCase)))
                    .Select(item => new AssignmentSubmissionsBatch(
                        item.AssignmentId,
                        item.Submissions.Select(AssignmentSubmissionSnapshotProjector.ToRecord).ToArray(),
                        item.ErrorCode,
                        item.ErrorMessage))
                    .ToArray();
                foreach (var item in request.PrefetchedSubmissions.Assignments.Where(item => !item.IsComplete))
                {
                    submissionFailures.Add($"{item.AssignmentId} ({item.ErrorCode ?? "snapshot_incomplete"})");
                }

                submissionReadFailed = request.PrefetchedSubmissions.Assignments.Any(item => !item.IsComplete);
            }
            else
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
        }

        var noGradeAssignmentIds = assignmentContexts
            .Where(context => IsNoGradeActivity(context.Module, assignmentSettings))
            .Select(context => context.Module.InstanceId!)
            .ToArray();
        if (request.IncludeAwaitingGrading && noGradeAssignmentIds.Length > 0)
        {
            if (IsForCourse(request.PrefetchedSubmissions, request.CourseId))
            {
                foreach (var assignmentId in noGradeAssignmentIds)
                {
                    var snapshotItem = request.PrefetchedSubmissions!.Assignments.FirstOrDefault(item =>
                        string.Equals(item.AssignmentId, assignmentId, StringComparison.OrdinalIgnoreCase));
                    if (snapshotItem?.Coverage?.GradesComplete == true)
                    {
                        feedbackReadyAssignments.Add(assignmentId);
                    }
                    else
                    {
                        feedbackReadFailures.Add(assignmentId);
                    }
                }
            }
            else if (gradeReadGateway is not null)
            {
                var feedbackReads = await ReadFeedbackByAssignmentAsync(
                    currentUserExternalId,
                    noGradeAssignmentIds,
                    studentMap.Keys,
                    feedbackReadFailures,
                    cancellationToken);
                foreach (var pair in feedbackReads)
                {
                    feedbackByAssignment[pair.Key] = pair.Value;
                    feedbackReadyAssignments.Add(pair.Key);
                }
            }
            else
            {
                foreach (var assignmentId in noGradeAssignmentIds)
                {
                    feedbackReadFailures.Add(assignmentId);
                }
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
            var returnedUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in batch.Submissions)
            {
                if (!string.IsNullOrWhiteSpace(record.UserId))
                {
                    returnedUserIds.Add(record.UserId);
                }

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
                        if (!feedbackReadyAssignments.Contains(module.InstanceId!))
                        {
                            noGradeFeedbackSkipped = true;
                            continue;
                        }

                        var feedback = record.CurrentFeedback;
                        if (feedbackByAssignment.TryGetValue(module.InstanceId!, out var grades) &&
                            grades.TryGetValue(record.UserId, out var existingGrade))
                        {
                            feedback = existingGrade.Feedback;
                        }

                        if (string.IsNullOrWhiteSpace(feedback) &&
                            gradingByStudent.TryGetValue(record.UserId, out var feedbackList))
                        {
                            feedbackList.Add(new AwaitingGradingItem(
                                module.InstanceId!,
                                module.Name,
                                dueDate,
                                record.ModifiedAt ?? record.CreatedAt));
                        }
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

            // `mod_assign_get_submissions` is allowed to omit users who have
            // no submission (especially when a status filter is supplied).
            // The assignment was read successfully, so an omitted active
            // student is an explicit not-submitted signal for this
            // student-facing query.  Do not apply this inference to
            // non-gradable activities, which are tracked through feedback
            // instead of delivery.
            if (!isNoGradeActivity)
            {
                foreach (var studentId in studentMap.Keys)
                {
                    if (returnedUserIds.Contains(studentId) ||
                        !pendingByStudent.TryGetValue(studentId, out var pendingList) ||
                        pendingList.Any(item => string.Equals(item.AssignmentId, module.InstanceId, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    pendingList.Add(pendingItem);
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
                return new AwaitingGradingSubmission(
                    request.CourseId,
                    item.AssignmentId,
                    item.AssignmentName,
                    kv.Key,
                    student.FullName,
                    student.LastCourseAccessAt,
                    "submitted",
                    item.SubmittedAt,
                    "awaiting_grading");
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

    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, AssignmentExistingGrade>>> ReadFeedbackByAssignmentAsync(
        string userExternalId,
        IReadOnlyCollection<string> assignmentIds,
        IEnumerable<string> studentIds,
        ISet<string> failures,
        CancellationToken cancellationToken)
    {
        const int maxConcurrency = 4;
        using var limiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var students = studentIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var tasks = assignmentIds.Select(async assignmentId =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                var grades = await gradeReadGateway!.GetExistingGradesAsync(
                    userExternalId,
                    assignmentId,
                    students,
                    cancellationToken);
                return (AssignmentId: assignmentId, Grades: grades, Success: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failures.Add(assignmentId);
                return (AssignmentId: assignmentId,
                    Grades: (IReadOnlyDictionary<string, AssignmentExistingGrade>)new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase),
                    Success: false);
            }
            finally
            {
                limiter.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results
            .Where(result => result.Success)
            .ToDictionary(result => result.AssignmentId, result => result.Grades, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsForCourse(CourseContentsSummary? contents, string courseId) =>
        contents is not null &&
        string.Equals(contents.CourseId, courseId, StringComparison.OrdinalIgnoreCase);

    private static bool IsForCourse(CourseParticipantsPage? participants, string courseId) =>
        participants is not null &&
        string.Equals(participants.CourseId, courseId, StringComparison.OrdinalIgnoreCase);

    private static bool IsForCourse(CourseAssignmentSubmissionsSnapshot? submissions, string courseId) =>
        submissions is not null &&
        string.Equals(submissions.CourseId, courseId, StringComparison.OrdinalIgnoreCase);

    private static bool IsNoGradeActivity(
        CourseModuleSummary module,
        IReadOnlyDictionary<string, AssignmentSettingsSummary> settings)
    {
        if (settings.TryGetValue(module.InstanceId ?? string.Empty, out var byInstance))
        {
            return !ResolveIsGradable(byInstance);
        }

        return settings.TryGetValue(module.ModuleId ?? string.Empty, out var byModule) &&
            !ResolveIsGradable(byModule);
    }

    private static IReadOnlyDictionary<string, AssignmentSettingsSummary>? GetSettingsFromSnapshot(
        CourseAssignmentSubmissionsSnapshot? snapshot,
        string courseId,
        IReadOnlyList<AssignmentContext> contexts)
    {
        if (!IsForCourse(snapshot, courseId))
        {
            return contexts.Count == 0
                ? new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
                : null;
        }

        var settings = new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal);
        foreach (var context in contexts)
        {
            var item = snapshot!.Assignments.FirstOrDefault(assignment =>
                string.Equals(assignment.AssignmentId, context.Module.InstanceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assignment.AssignmentModuleId, context.Module.ModuleId, StringComparison.OrdinalIgnoreCase));
            if (item?.MaxGrade is not { } maxGrade)
            {
                return null;
            }

            var summary = new AssignmentSettingsSummary(
                item.AssignmentId,
                maxGrade,
                item.AssignmentName,
                item.IsGradable);
            settings[item.AssignmentId] = summary;
            if (!string.IsNullOrWhiteSpace(item.AssignmentModuleId))
            {
                settings[item.AssignmentModuleId] = summary;
            }
        }

        return settings;
    }

    private static bool ResolveIsGradable(AssignmentSettingsSummary settings) =>
        settings.IsGradable ?? settings.MaxGrade > 0;

    private sealed record AssignmentContext(CourseModuleSummary Module, DateTimeOffset? DueDate);
}
