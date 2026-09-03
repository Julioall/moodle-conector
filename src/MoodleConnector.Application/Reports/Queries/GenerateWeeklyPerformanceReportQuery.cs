using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Reports.Queries;

// ── Result types ─────────────────────────────────────────────────────────────

/// <summary>
/// Linha de desempenho semanal de um estudante.
/// </summary>
public sealed record StudentWeeklyPerformanceRow(
    string StudentId,
    string FullName,
    DateTimeOffset? LastCourseAccessAt,
    int? DaysWithoutAccess,
    bool NeverAccessed,
    int TotalAssignments,
    int SubmittedCount,
    int PendingCount,
    int BelowMinimumCount,
    IReadOnlyList<StudentGradeItem> BelowMinimumItems,
    IReadOnlyList<string> PendingAssignmentNames,
    string AttentionLevel)  // "ok" | "attention" | "risk"
{
    public int AwaitingGradingCount { get; init; }
    public IReadOnlyList<string> AwaitingGradingAssignmentNames { get; init; } = [];
    public int NotSubmittedCount { get; init; }
    public int ReviewedWithFeedbackCount { get; init; }
    public int GradedCount { get; init; }
}

public sealed record GenerateWeeklyPerformanceReportResult(
    string CourseId,
    DateTimeOffset GeneratedAt,
    int TotalStudents,
    int StudentsWithAttention,
    int StudentsAtRisk,
    decimal MinGradePercent,
    int InactiveDaysThreshold,
    IReadOnlyList<StudentWeeklyPerformanceRow> Students,
    IReadOnlyList<string> SuggestedRecipientIdsForAccess,
    IReadOnlyList<string> SuggestedRecipientIdsForGrade,
    IReadOnlyList<string> SuggestedRecipientIdsForPending,
    string? Warning);

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Gera relatório semanal consolidado de desempenho da turma.
/// Cruza acesso, entregas pendentes e notas abaixo do mínimo por estudante.
///
/// Estratégia:
/// 1. Busca lista de estudantes ativos.
/// 2. Para cada estudante, coleta o boletim (notas por SA).
/// 3. Cruza com dados de acesso (LastCourseAccessAt) da listagem de participantes.
/// 4. Calcula nível de atenção: "ok", "attention" (1 indicador), "risk" (2+ indicadores).
///
/// Limitações declaradas:
/// - Uma SA só entra como pendente quando não há nota nem metadado de envio;
///   entregas aguardando correção são expostas separadamente.
/// - Dados de acesso dependem do campo lastcourseaccess da API Moodle.
/// - Pode ser lento para turmas grandes (uma chamada por estudante ao gradebook).
/// </summary>
public sealed record GenerateWeeklyPerformanceReportQuery(
    string CourseId,
    decimal MinGradePercent = 60m,
    int InactiveDaysThreshold = 7,
    int MaxStudentsToAnalyze = 60) : IRequest<GenerateWeeklyPerformanceReportResult>;

public sealed class GenerateWeeklyPerformanceReportQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleGradebookGateway gradebookGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway,
    IMediator? mediator = null)
    : IRequestHandler<GenerateWeeklyPerformanceReportQuery, GenerateWeeklyPerformanceReportResult>
{
    private const string LimitationMessage =
        "Relatório gerado com base nos dados disponíveis na API Moodle. " +
        "Notas sem lançamento aparecem como 'sem nota'; entregas identificadas como aguardando correção não entram como pendência. " +
        "Acesso ao AVA depende do campo lastcourseaccess disponível no Moodle. " +
        "Atividades cujo nome indica recuperação não são tratadas como pendência geral. " +
        "Para turmas grandes o relatório pode ser lento (uma consulta de boletim por estudante).";

    public async Task<GenerateWeeklyPerformanceReportResult> Handle(
        GenerateWeeklyPerformanceReportQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();
        var now = DateTimeOffset.UtcNow;

        // 1. Fetch active students
        var participantsPage = await participantsGateway.GetCourseParticipantsAsync(
            userExternalId: currentUserExternalId,
            courseId: request.CourseId,
            statusFilter: ParticipantStatusFilter.Active,
            page: 0,
            pageSize: request.MaxStudentsToAnalyze > 0 ? request.MaxStudentsToAnalyze : 60,
            studentsOnly: true,
            includeEmail: false,
            groupId: null,
            cancellationToken: cancellationToken);

        if (participantsPage.Participants.Count == 0)
        {
            return new GenerateWeeklyPerformanceReportResult(
                CourseId: request.CourseId,
                GeneratedAt: now,
                TotalStudents: 0,
                StudentsWithAttention: 0,
                StudentsAtRisk: 0,
                MinGradePercent: request.MinGradePercent,
                InactiveDaysThreshold: request.InactiveDaysThreshold,
                Students: [],
                SuggestedRecipientIdsForAccess: [],
                SuggestedRecipientIdsForGrade: [],
                SuggestedRecipientIdsForPending: [],
                Warning: "Nenhum estudante ativo encontrado no curso.");
        }

        var rows = new List<StudentWeeklyPerformanceRow>();
        var recipientsForAccess = new List<string>();
        var recipientsForGrade = new List<string>();
        var recipientsForPending = new List<string>();
        var submissionState = await CourseSubmissionReportState.LoadAsync(
            mediator,
            request.CourseId,
            request.MaxStudentsToAnalyze > 0 ? request.MaxStudentsToAnalyze : 60,
            cancellationToken);

        // 2. Per-student analysis
        foreach (var student in participantsPage.Participants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // -- Access analysis
            int? daysWithoutAccess = null;
            bool neverAccessed = false;
            bool isInactive = false;

            if (!student.LastCourseAccessAt.HasValue)
            {
                neverAccessed = true;
                isInactive = true;
            }
            else
            {
                daysWithoutAccess = (int)(now - student.LastCourseAccessAt.Value).TotalDays;
                isInactive = daysWithoutAccess >= request.InactiveDaysThreshold;
            }

            // -- Gradebook analysis (best effort)
            IReadOnlyList<StudentGradeItem> belowMinimumItems = [];
            int totalAssignments = 0;
            int pendingCount = 0;
            var pendingNames = new List<string>();
            var awaitingGradingNames = new List<string>();

            try
            {
                var gradebook = await gradebookGateway.GetStudentGradebookAsync(
                    request.CourseId, student.UserId, cancellationToken);

                var activityItems = gradebook.Items
                    .Where(GradebookMappingHelper.IsEvaluativeReportActivityItem)
                    .ToList();

                totalAssignments = activityItems.Count;

                var gradeItems = activityItems
                    .Select(i => GradebookMappingHelper.ToStudentGradeItem(i, request.MinGradePercent))
                    .ToList();

                belowMinimumItems = gradeItems.Where(i => i.BelowMinimum).ToList();

                var courseTotal = gradebook.Items.FirstOrDefault(GradebookMappingHelper.IsCourseTotalItem);
                if (courseTotal is not null)
                {
                    var courseTotalItem = GradebookMappingHelper.ToStudentGradeItem(courseTotal, request.MinGradePercent);
                    if (courseTotalItem.BelowMinimum)
                    {
                        belowMinimumItems = [courseTotalItem, ..belowMinimumItems];
                    }
                }

                // Compatibility fallback for unit hosts that do not register
                // MediatR. Production uses the assignment submission state
                // below, which is authoritative for delivery vs. grading.
                if (!submissionState.IsAvailable)
                {
                    var pending = activityItems
                        .Where(GradebookMappingHelper.IsConfirmedPending)
                        .ToList();
                    pendingCount = pending.Count;
                    pendingNames = pending.Select(i => i.ItemName).ToList();
                    awaitingGradingNames = activityItems
                        .Where(GradebookMappingHelper.IsAwaitingGrading)
                        .Select(i => i.ItemName)
                        .ToList();
                }
            }
            catch
            {
                // Gradebook unavailable for this student — continue with partial data
            }

            if (submissionState.IsAvailable)
            {
                var pending = submissionState.PendingFor(student.UserId);
                var awaiting = submissionState.AwaitingFor(student.UserId);
                pendingCount = pending.Count;
                pendingNames = pending.Select(item => item.AssignmentName).ToList();
                awaitingGradingNames = awaiting.Select(item => item.AssignmentName).ToList();
            }

            int submittedCount = totalAssignments - pendingCount;

            // -- Attention level calculation
            int riskFactors = 0;
            if (isInactive) riskFactors++;
            if (belowMinimumItems.Count > 0) riskFactors++;
            if (pendingCount > 0) riskFactors++;

            var attentionLevel = riskFactors switch
            {
                0 => "ok",
                1 => "attention",
                _ => "risk"
            };

            rows.Add(new StudentWeeklyPerformanceRow(
                StudentId: student.UserId,
                FullName: student.FullName,
                LastCourseAccessAt: student.LastCourseAccessAt,
                DaysWithoutAccess: daysWithoutAccess,
                NeverAccessed: neverAccessed,
                TotalAssignments: totalAssignments,
                SubmittedCount: submittedCount,
                PendingCount: pendingCount,
                BelowMinimumCount: belowMinimumItems.Count,
                BelowMinimumItems: belowMinimumItems,
                PendingAssignmentNames: pendingNames,
                AttentionLevel: attentionLevel)
            {
                AwaitingGradingCount = awaitingGradingNames.Count,
                AwaitingGradingAssignmentNames = awaitingGradingNames,
                NotSubmittedCount = submissionState.IsAvailable
                    ? submissionState.CountFor(student.UserId, SubmissionEvaluationState.NotSubmitted)
                    : pendingCount,
                ReviewedWithFeedbackCount = submissionState.IsAvailable
                    ? submissionState.CountFor(student.UserId, SubmissionEvaluationState.ReviewedWithFeedback)
                    : 0,
                GradedCount = submissionState.IsAvailable
                    ? submissionState.CountFor(student.UserId, SubmissionEvaluationState.GradedNumeric)
                    : 0
            });

            if (isInactive) recipientsForAccess.Add(student.UserId);
            if (belowMinimumItems.Count > 0) recipientsForGrade.Add(student.UserId);
            if (pendingCount > 0) recipientsForPending.Add(student.UserId);
        }

        // Sort: risk first, then attention, then ok
        var sorted = rows
            .OrderBy(r => r.AttentionLevel switch { "risk" => 0, "attention" => 1, _ => 2 })
            .ThenByDescending(r => r.BelowMinimumCount + r.PendingCount)
            .ToList();

        int studentsAtRisk = sorted.Count(r => r.AttentionLevel == "risk");
        int studentsWithAttention = sorted.Count(r => r.AttentionLevel == "attention");

        return new GenerateWeeklyPerformanceReportResult(
            CourseId: request.CourseId,
            GeneratedAt: now,
            TotalStudents: sorted.Count,
            StudentsWithAttention: studentsWithAttention,
            StudentsAtRisk: studentsAtRisk,
            MinGradePercent: request.MinGradePercent,
            InactiveDaysThreshold: request.InactiveDaysThreshold,
            Students: sorted,
            SuggestedRecipientIdsForAccess: recipientsForAccess,
            SuggestedRecipientIdsForGrade: recipientsForGrade,
            SuggestedRecipientIdsForPending: recipientsForPending,
            Warning: submissionState.IsAvailable && !submissionState.IsComplete
                ? $"{LimitationMessage} {submissionState.Warning ?? "A cobertura de entregas está incompleta."}"
                : LimitationMessage);
    }
}
