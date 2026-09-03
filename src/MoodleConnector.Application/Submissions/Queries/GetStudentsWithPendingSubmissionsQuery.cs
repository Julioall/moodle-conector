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

/// <summary>Canonical submission state consumed by pending tools and reports.</summary>
public sealed record SubmissionEvaluationItem(
    string CourseId,
    string AssignmentId,
    string AssignmentName,
    string StudentId,
    SubmissionEvaluationState State,
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
    public IReadOnlyList<AwaitingGradingSubmission> AwaitingGrading { get; init; } = [];
    public IReadOnlyList<SubmissionEvaluationItem> Evaluations { get; init; } = [];
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
    IMoodleAssignmentGradeReadGateway? gradeReadGateway = null,
    IMoodleGradebookGateway? gradebookGateway = null)
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

        // Grade/feedback evidence is fetched for every assignment when
        // awaiting-grading is requested. A null grade is not enough to say an
        // assignment is awaiting correction: feedback and grade timestamps
        // may prove it has already been reviewed.
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

        var assignmentIdsForEvidence = assignmentContexts
            .Select(context => context.Module.InstanceId!)
            .ToArray();
        if (request.IncludeAwaitingGrading && assignmentIdsForEvidence.Length > 0)
        {
            if (IsForCourse(request.PrefetchedSubmissions, request.CourseId))
            {
                foreach (var assignmentId in assignmentIdsForEvidence)
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
                    assignmentIdsForEvidence,
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
                foreach (var assignmentId in assignmentIdsForEvidence)
                {
                    feedbackReadFailures.Add(assignmentId);
                }
            }
        }

        var gradebooks = request.IncludeAwaitingGrading
            ? await ReadGradebooksAsync(request.CourseId, studentMap.Keys, cancellationToken)
            : null;

        var contextsByAssignmentId = assignmentContexts
            .ToDictionary(context => context.Module.InstanceId!, StringComparer.Ordinal);
        var evaluations = new List<SubmissionEvaluationItem>();

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
            var returnedUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in batch.Submissions)
            {
                if (!string.IsNullOrWhiteSpace(record.UserId))
                {
                    returnedUserIds.Add(record.UserId);
                }

                AssignmentExistingGrade? existingGrade = null;
                feedbackByAssignment.TryGetValue(module.InstanceId!, out var grades);
                grades?.TryGetValue(record.UserId, out existingGrade);
                CourseGradebook? gradebook = null;
                gradebooks?.TryGetValue(record.UserId, out gradebook);
                var gradebookItem = FindAssignmentGradebookItem(gradebook, module.InstanceId, module.ModuleId);
                var state = SubmissionEvaluationStateResolver.Resolve(new SubmissionEvaluationEvidence(
                    HasSubmission: ToSubmissionPresence(record.Status),
                    GradeRaw: gradebookItem?.GradeRaw ?? (existingGrade?.HasGrade == true ? existingGrade.Grade : null),
                    GradedDateGraded: gradebookItem?.GradedDateGraded,
                    Feedback: gradebookItem?.Feedback ?? existingGrade?.Feedback ?? record.CurrentFeedback,
                    ReviewEvidenceAvailable: !request.IncludeAwaitingGrading ||
                        gradebooks is not null || feedbackReadyAssignments.Contains(module.InstanceId!),
                    GradingStatus: record.GradingStatus,
                    GraderId: existingGrade?.GraderId ?? ParseGraderId(gradebookItem?.GraderId) ?? record.CurrentGraderId,
                    GradeTimeModified: existingGrade?.TimeModified ?? gradebookItem?.GradedDateGraded ?? record.CurrentGradeTimeModified,
                    SubmissionTimeModified: record.ModifiedAt?.ToUnixTimeSeconds()));
                if (studentMap.ContainsKey(record.UserId))
                {
                    evaluations.Add(new SubmissionEvaluationItem(
                        request.CourseId, module.InstanceId!, module.Name, record.UserId, state,
                        record.ModifiedAt ?? record.CreatedAt));
                }

                if (state == SubmissionEvaluationState.NotSubmitted)
                {
                    if (pendingByStudent.TryGetValue(record.UserId, out var pendingList))
                        pendingList.Add(pendingItem);
                }
                else if (request.IncludeAwaitingGrading && state == SubmissionEvaluationState.AwaitingGrading)
                {
                    if (gradingByStudent.TryGetValue(record.UserId, out var gradingList))
                    {
                        gradingList.Add(new AwaitingGradingItem(
                            module.InstanceId!,
                            module.Name,
                            dueDate,
                            record.ModifiedAt ?? record.CreatedAt));
                    }
                }
            }

            // A successful submission batch makes an omitted active student a
            // known NOT_SUBMITTED state. Do not apply this inference to a
            // failed or incomplete assignment read.
            foreach (var studentId in studentMap.Keys)
            {
                if (returnedUserIds.Contains(studentId) ||
                    !pendingByStudent.TryGetValue(studentId, out var pendingList) ||
                    pendingList.Any(item => string.Equals(item.AssignmentId, module.InstanceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                pendingList.Add(pendingItem);
                evaluations.Add(new SubmissionEvaluationItem(
                    request.CourseId, module.InstanceId!, module.Name, studentId,
                    SubmissionEvaluationState.NotSubmitted, null));
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
            Evaluations = evaluations,
            IsComplete = !submissionReadFailed && submissionFailures.Count == 0,
        };
    }

    private static bool? ToSubmissionPresence(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "submitted" => true,
        "new" or "draft" or "reopened" or "notsubmitted" or "not_submitted" => false,
        _ => null
    };

    private async Task<IReadOnlyDictionary<string, CourseGradebook>?> ReadGradebooksAsync(
        string courseId,
        IEnumerable<string> studentIds,
        CancellationToken cancellationToken)
    {
        if (gradebookGateway is null)
        {
            return null;
        }

        const int maxConcurrency = 6;
        using var gate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        try
        {
            var reads = studentIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(async studentId =>
            {
                await gate.WaitAsync(cancellationToken);
                try { return await gradebookGateway.GetStudentGradebookAsync(courseId, studentId, cancellationToken); }
                finally { gate.Release(); }
            });
            var results = await Task.WhenAll(reads);
            return results.ToDictionary(item => item.StudentId, StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static GradebookItem? FindAssignmentGradebookItem(
        CourseGradebook? gradebook,
        string? assignmentInstanceId,
        string? assignmentModuleId) =>
        gradebook?.Items.FirstOrDefault(item =>
            string.Equals(item.ItemModule, "assign", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(item.ItemInstance, assignmentInstanceId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.CourseModuleId, assignmentModuleId, StringComparison.OrdinalIgnoreCase)));

    private static long? ParseGraderId(string? value) =>
        long.TryParse(value, out var graderId) ? graderId : null;

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
