using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Reports.Queries;

// ── Result types ─────────────────────────────────────────────────────────────

/// <summary>
/// Situação de um estudante para o conselho de classe.
/// </summary>
public sealed record StudentClassCouncilRow(
    string StudentId,
    string FullName,
    DateTimeOffset? LastCourseAccessAt,
    int? DaysWithoutAccess,
    bool NeverAccessed,
    int TotalGradedItems,
    int BelowMinimumCount,
    int PendingItemsCount,
    IReadOnlyList<StudentGradeItem> BelowMinimumItems,
    string SituationFlag,   // "regular" | "attention" | "recovery_needed" | "at_risk"
    IReadOnlyList<string> Recommendations);

public sealed record GenerateClassCouncilReportResult(
    string CourseId,
    DateTimeOffset GeneratedAt,
    int TotalStudents,
    int Regular,
    int NeedAttention,
    int NeedRecovery,
    int AtRisk,
    decimal MinGradePercent,
    IReadOnlyList<StudentClassCouncilRow> Students,
    string Disclaimer,
    string? Warning);

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Gera relatório para conselho de classe com situação pedagógica de cada estudante.
///
/// Classificações:
/// - "regular": acessa, entregou, notas acima do mínimo.
/// - "attention": 1 indicador negativo (acesso ou nota ou pendência).
/// - "recovery_needed": tem pelo menos 1 SA abaixo do mínimo (critério de recuperação paralela).
/// - "at_risk": 2 ou mais indicadores negativos simultaneamente.
///
/// IMPORTANTE: O conector não decide aprovação, reprovação ou evasão.
/// A situação é indicativa e deve ser interpretada pelo tutor e docente presencial.
/// </summary>
public sealed record GenerateClassCouncilReportQuery(
    string CourseId,
    decimal MinGradePercent = 60m,
    int InactiveDaysThreshold = 7,
    int MaxStudentsToAnalyze = 60) : IRequest<GenerateClassCouncilReportResult>;

public sealed class GenerateClassCouncilReportQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleGradebookGateway gradebookGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GenerateClassCouncilReportQuery, GenerateClassCouncilReportResult>
{
    private const string Disclaimer =
        "Este relatório é indicativo e não constitui decisão oficial de aprovação, reprovação ou evasão. " +
        "Os dados devem ser interpretados pelo tutor e pelo docente presencial. " +
        "Situações de recuperação paralela devem ser tratadas conforme as normas pedagógicas vigentes. " +
        "Atividades cujo nome indica recuperação não são tratadas como pendência geral.";

    public async Task<GenerateClassCouncilReportResult> Handle(
        GenerateClassCouncilReportQuery request,
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
            return new GenerateClassCouncilReportResult(
                CourseId: request.CourseId, GeneratedAt: now,
                TotalStudents: 0, Regular: 0, NeedAttention: 0, NeedRecovery: 0, AtRisk: 0,
                MinGradePercent: request.MinGradePercent,
                Students: [], Disclaimer: Disclaimer,
                Warning: "Nenhum estudante ativo encontrado no curso.");
        }

        var rows = new List<StudentClassCouncilRow>();

        foreach (var student in participantsPage.Participants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Access
            int? daysWithoutAccess = null;
            bool neverAccessed = !student.LastCourseAccessAt.HasValue;
            if (student.LastCourseAccessAt.HasValue)
                daysWithoutAccess = (int)(now - student.LastCourseAccessAt.Value).TotalDays;
            bool isInactive = neverAccessed || (daysWithoutAccess >= request.InactiveDaysThreshold);

            // Gradebook
            IReadOnlyList<StudentGradeItem> belowMinimumItems = [];
            int totalGradedItems = 0;
            int pendingItemsCount = 0;
            try
            {
                var gradebook = await gradebookGateway.GetStudentGradebookAsync(
                    request.CourseId, student.UserId, cancellationToken);

                var activityItems = gradebook.Items
                    .Where(GradebookMappingHelper.IsDerivedReportActivityItem)
                    .ToList();
                totalGradedItems = activityItems.Count;

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
                pendingItemsCount = gradeItems.Count(i => !i.GradeRaw.HasValue);
            }
            catch { /* partial data */ }

            // Classification
            bool hasLowGrade = belowMinimumItems.Count > 0;
            int negativeFactors = (isInactive ? 1 : 0) + (hasLowGrade ? 1 : 0) + (pendingItemsCount > 0 ? 1 : 0);

            var situationFlag = negativeFactors switch
            {
                0 => "regular",
                1 when hasLowGrade => "recovery_needed",
                1 => "attention",
                _ => "at_risk"
            };

            // Recommendations
            var recommendations = BuildRecommendations(isInactive, hasLowGrade, pendingItemsCount, request.InactiveDaysThreshold);

            rows.Add(new StudentClassCouncilRow(
                StudentId: student.UserId,
                FullName: student.FullName,
                LastCourseAccessAt: student.LastCourseAccessAt,
                DaysWithoutAccess: daysWithoutAccess,
                NeverAccessed: neverAccessed,
                TotalGradedItems: totalGradedItems,
                BelowMinimumCount: belowMinimumItems.Count,
                PendingItemsCount: pendingItemsCount,
                BelowMinimumItems: belowMinimumItems,
                SituationFlag: situationFlag,
                Recommendations: recommendations));
        }

        var sorted = rows
            .OrderBy(r => r.SituationFlag switch
            {
                "at_risk" => 0,
                "recovery_needed" => 1,
                "attention" => 2,
                _ => 3
            })
            .ToList();

        return new GenerateClassCouncilReportResult(
            CourseId: request.CourseId,
            GeneratedAt: now,
            TotalStudents: sorted.Count,
            Regular: sorted.Count(r => r.SituationFlag == "regular"),
            NeedAttention: sorted.Count(r => r.SituationFlag == "attention"),
            NeedRecovery: sorted.Count(r => r.SituationFlag == "recovery_needed"),
            AtRisk: sorted.Count(r => r.SituationFlag == "at_risk"),
            MinGradePercent: request.MinGradePercent,
            Students: sorted,
            Disclaimer: Disclaimer,
            Warning: null);
    }

    private static IReadOnlyList<string> BuildRecommendations(
        bool isInactive, bool hasLowGrade, int pendingCount, int inactiveDays)
    {
        var recs = new List<string>();
        if (isInactive)
            recs.Add($"Estudante sem acesso há mais de {inactiveDays} dias. Verificar contato e engajamento.");
        if (hasLowGrade)
            recs.Add("Possui SA(s) com conceito abaixo do mínimo. Verificar elegibilidade para recuperação paralela.");
        if (pendingCount > 0)
            recs.Add($"{pendingCount} SA(s) sem nota registrada. Verificar se foram entregues ou ainda estão em aberto.");
        if (recs.Count == 0)
            recs.Add("Situação regular. Manter acompanhamento periódico.");
        return recs;
    }
}
