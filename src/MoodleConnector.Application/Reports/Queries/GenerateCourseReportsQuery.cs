using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Reports.Queries;

// ── Result types ─────────────────────────────────────────────────────────────

public sealed record CourseOverviewResult(
    string CourseId,
    DateTimeOffset GeneratedAt,
    int TotalActiveStudents,
    int StudentsWhoAccessed,
    int StudentsNeverAccessed,
    int StudentsInactiveDays,
    int InactiveDaysThreshold,
    int? TotalGradedItems,
    decimal? AverageBelowMinimumPerStudent,
    IReadOnlyList<string> SuggestedActionsForTutor,
    string? Warning);

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Gera resumo executivo do curso: participantes, acesso e indicadores gerais.
/// Não consulta gradebook individual — usa apenas dados de participants para ser rápido.
/// </summary>
public sealed record GenerateCourseOverviewQuery(
    string CourseId,
    int InactiveDaysThreshold = 7,
    int MaxStudentsToAnalyze = 100) : IRequest<CourseOverviewResult>;

public sealed class GenerateCourseOverviewQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GenerateCourseOverviewQuery, CourseOverviewResult>
{
    public async Task<CourseOverviewResult> Handle(
        GenerateCourseOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();
        var now = DateTimeOffset.UtcNow;

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

        var students = participantsPage.Participants;
        int total = students.Count;

        if (total == 0)
        {
            return new CourseOverviewResult(
                CourseId: request.CourseId, GeneratedAt: now,
                TotalActiveStudents: 0, StudentsWhoAccessed: 0,
                StudentsNeverAccessed: 0, StudentsInactiveDays: 0,
                InactiveDaysThreshold: request.InactiveDaysThreshold,
                TotalGradedItems: null, AverageBelowMinimumPerStudent: null,
                SuggestedActionsForTutor: ["Nenhum estudante ativo encontrado. Verificar matrículas."],
                Warning: "Nenhum estudante ativo encontrado no curso. O resumo rápido não calcula métricas de nota.");
        }

        int neverAccessed = students.Count(s => !s.LastCourseAccessAt.HasValue);
        int inactiveDays = students.Count(s =>
            s.LastCourseAccessAt.HasValue &&
            (int)(now - s.LastCourseAccessAt.Value).TotalDays >= request.InactiveDaysThreshold);
        int accessed = total - neverAccessed;

        var actions = new List<string>();
        double inactivePct = (neverAccessed + inactiveDays) * 100.0 / total;

        if (inactivePct > 30)
            actions.Add($"{neverAccessed + inactiveDays} estudantes sem acesso recente ({inactivePct:F0}% da turma). Priorizar contato.");
        else if (inactivePct > 10)
            actions.Add($"{neverAccessed + inactiveDays} estudantes inativos. Enviar mensagem de acompanhamento.");
        else
            actions.Add("Acesso da turma satisfatório. Manter acompanhamento periódico.");

        if (neverAccessed > 0)
            actions.Add($"{neverAccessed} estudantes nunca acessaram o AVA. Verificar problemas de acesso ou desistência.");

        return new CourseOverviewResult(
            CourseId: request.CourseId,
            GeneratedAt: now,
            TotalActiveStudents: total,
            StudentsWhoAccessed: accessed,
            StudentsNeverAccessed: neverAccessed,
            StudentsInactiveDays: inactiveDays,
            InactiveDaysThreshold: request.InactiveDaysThreshold,
            // This fast overview intentionally does not call each student's
            // gradebook. Null means not calculated; returning zero made an
            // unavailable metric indistinguishable from a real zero.
            TotalGradedItems: null,
            AverageBelowMinimumPerStudent: null,
            SuggestedActionsForTutor: actions,
            Warning: !students.Any(s => s.LastCourseAccessAt.HasValue)
                ? "O campo de último acesso ao curso (lastcourseaccess) pode não estar disponível para esta configuração de Moodle. O resumo rápido não calcula métricas de nota; use o relatório de notas do curso."
                : "O resumo rápido não calcula métricas de nota; use o relatório de notas do curso.");
    }
}

// ── Post-Execution result types ───────────────────────────────────────────────

public sealed record PostExecutionStudentRow(
    string StudentId,
    string FullName,
    bool NeverAccessed,
    int? DaysWithoutAccess,
    int TotalGradedItems,
    int BelowMinimumCount,
    int PendingCount,
    string OutcomeIndicator);  // "likely_complete" | "pending_recovery" | "at_risk" | "unknown"

public sealed record GeneratePostExecutionReportResult(
    string CourseId,
    DateTimeOffset GeneratedAt,
    int TotalStudents,
    int LikelyComplete,
    int PendingRecovery,
    int AtRisk,
    int Unknown,
    decimal MinGradePercent,
    IReadOnlyList<PostExecutionStudentRow> Students,
    string Disclaimer,
    string? Warning);

// ── Post-Execution Query ──────────────────────────────────────────────────────

/// <summary>
/// Gera relatório de pós-execução: situação provável de cada estudante ao fim do curso.
///
/// Classificações indicativas (não constituem decisão oficial):
/// - "likely_complete": acessa, notas acima do mínimo, sem pendências.
/// - "pending_recovery": notas abaixo do mínimo em pelo menos 1 SA.
/// - "at_risk": não acessa + pendências ou notas baixas.
/// - "unknown": dados insuficientes para classificar.
/// </summary>
public sealed record GeneratePostExecutionReportQuery(
    string CourseId,
    decimal MinGradePercent = 60m,
    int MaxStudentsToAnalyze = 60) : IRequest<GeneratePostExecutionReportResult>;

public sealed class GeneratePostExecutionReportQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleGradebookGateway gradebookGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GeneratePostExecutionReportQuery, GeneratePostExecutionReportResult>
{
    private const string Disclaimer =
        "Este relatório é indicativo e não constitui decisão oficial de conclusão, reprovação ou evasão. " +
        "Os dados são baseados na API Moodle e devem ser validados pelo tutor e pela coordenação. " +
        "Atividades cujo nome indica recuperação não são tratadas como pendência geral.";

    public async Task<GeneratePostExecutionReportResult> Handle(
        GeneratePostExecutionReportQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();
        var now = DateTimeOffset.UtcNow;

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
            return new GeneratePostExecutionReportResult(
                CourseId: request.CourseId, GeneratedAt: now,
                TotalStudents: 0, LikelyComplete: 0, PendingRecovery: 0, AtRisk: 0, Unknown: 0,
                MinGradePercent: request.MinGradePercent,
                Students: [], Disclaimer: Disclaimer,
                Warning: "Nenhum estudante ativo encontrado.");
        }

        var rows = new List<PostExecutionStudentRow>();

        foreach (var student in participantsPage.Participants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool neverAccessed = !student.LastCourseAccessAt.HasValue;
            int? daysWithoutAccess = student.LastCourseAccessAt.HasValue
                ? (int)(now - student.LastCourseAccessAt.Value).TotalDays
                : null;

            int totalGradedItems = 0, belowMinimumCount = 0, pendingCount = 0;
            bool hasGradebookData = false;

            try
            {
                var gradebook = await gradebookGateway.GetStudentGradebookAsync(
                    request.CourseId, student.UserId, cancellationToken);

                var activityItems = gradebook.Items
                    .Where(GradebookMappingHelper.IsDerivedReportActivityItem)
                    .ToList();

                totalGradedItems = activityItems.Count;
                hasGradebookData = gradebook.Items.Any(GradebookMappingHelper.IsDerivedReportItem);

                var gradeItems = activityItems
                    .Select(i => GradebookMappingHelper.ToStudentGradeItem(i, request.MinGradePercent))
                    .ToList();

                belowMinimumCount = gradeItems.Count(i => i.BelowMinimum);
                var courseTotal = gradebook.Items.FirstOrDefault(GradebookMappingHelper.IsCourseTotalItem);
                if (courseTotal is not null)
                {
                    var courseTotalItem = GradebookMappingHelper.ToStudentGradeItem(courseTotal, request.MinGradePercent);
                    if (courseTotalItem.BelowMinimum)
                    {
                        belowMinimumCount++;
                    }
                }
                pendingCount = gradeItems.Count(i => !i.GradeRaw.HasValue);
            }
            catch { /* partial data */ }

            var outcome = DetermineOutcome(neverAccessed, belowMinimumCount, pendingCount, hasGradebookData);

            rows.Add(new PostExecutionStudentRow(
                StudentId: student.UserId,
                FullName: student.FullName,
                NeverAccessed: neverAccessed,
                DaysWithoutAccess: daysWithoutAccess,
                TotalGradedItems: totalGradedItems,
                BelowMinimumCount: belowMinimumCount,
                PendingCount: pendingCount,
                OutcomeIndicator: outcome));
        }

        var sorted = rows.OrderBy(r => r.OutcomeIndicator switch
        {
            "at_risk" => 0,
            "pending_recovery" => 1,
            "unknown" => 2,
            _ => 3
        }).ToList();

        return new GeneratePostExecutionReportResult(
            CourseId: request.CourseId,
            GeneratedAt: now,
            TotalStudents: sorted.Count,
            LikelyComplete: sorted.Count(r => r.OutcomeIndicator == "likely_complete"),
            PendingRecovery: sorted.Count(r => r.OutcomeIndicator == "pending_recovery"),
            AtRisk: sorted.Count(r => r.OutcomeIndicator == "at_risk"),
            Unknown: sorted.Count(r => r.OutcomeIndicator == "unknown"),
            MinGradePercent: request.MinGradePercent,
            Students: sorted,
            Disclaimer: Disclaimer,
            Warning: null);
    }

    private static string DetermineOutcome(bool neverAccessed, int belowMin, int pending, bool hasData)
    {
        if (!hasData) return "unknown";
        if (neverAccessed && belowMin > 0) return "at_risk";
        if (belowMin > 0) return "pending_recovery";
        // A null grade alone does not establish that the student missed an
        // activity: it can be a submitted assignment awaiting teacher review.
        // Keep the row visible, but require a confirmed below-minimum grade
        // before recommending recovery.
        if (pending > 0) return "unknown";
        return "likely_complete";
    }
}
