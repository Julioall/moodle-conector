using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Risk.Queries;

public sealed class GetStudentsAtRiskReportQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleGradebookGateway gradebookGateway,
    IMoodleCompletionGateway completionGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GetStudentsAtRiskReportQuery, StudentsAtRiskReportResult>
{
    public async Task<StudentsAtRiskReportResult> Handle(GetStudentsAtRiskReportQuery request, CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();

        // Fetch students (max 100 to avoid API rate limits for now)
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

        var reports = new List<StudentRiskReport>();
        var gradebookFailureCount = 0;
        var completionFailureCount = 0;

        foreach (var student in participantsPage.Participants)
        {
            var riskLevel = RiskLevel.Baixo;
            var factors = new List<string>();

            // 1. Inactivity Risk
            var inactiveDays = student.LastCourseAccessAt.HasValue
                ? (DateTimeOffset.UtcNow - student.LastCourseAccessAt.Value).TotalDays
                : -1; // Never accessed

            if (inactiveDays == -1)
            {
                factors.Add("Estudante nunca acessou o curso.");
                riskLevel = RiskLevel.Alto;
            }
            else if (inactiveDays > request.InactivityThresholdDays)
            {
                factors.Add($"Estudante inativo por {(int)inactiveDays} dias (limite: {request.InactivityThresholdDays}).");
                riskLevel = riskLevel < RiskLevel.Alto ? RiskLevel.Medio : riskLevel;
            }

            // Fetch Gradebook
            decimal? currentGrade = null;
            try
            {
                var gradebook = await gradebookGateway.GetStudentGradebookAsync(request.CourseId, student.UserId, cancellationToken);
                var courseGradeItem = gradebook.Items.FirstOrDefault(i => i.ItemType == "course");

                if (courseGradeItem != null)
                {
                    currentGrade = GradebookMappingHelper.ResolvePercentage(courseGradeItem);
                    if (currentGrade.HasValue && currentGrade.Value < request.MinGradePercentage)
                    {
                        factors.Add($"Nota atual ({currentGrade.Value:F1}%) abaixo da media minima esperada ({request.MinGradePercentage:F1}%).");
                        riskLevel = riskLevel < RiskLevel.Alto ? RiskLevel.Medio : riskLevel;
                        if (currentGrade.Value < request.MinGradePercentage - 20) // Much lower
                        {
                            riskLevel = RiskLevel.Alto;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Gradebook might be disabled or unreachable for this student
                gradebookFailureCount++;
            }

            // Fetch Completion
            decimal? completionRate = null;
            try
            {
                var completion = await completionGateway.GetStudentCompletionAsync(request.CourseId, student.UserId, cancellationToken);
                var totalActivities = completion.Activities.Count;
                if (totalActivities > 0)
                {
                    var completedActivities = completion.Activities.Count(a => a.State == 1 || a.State == 2);
                    completionRate = (decimal)completedActivities / totalActivities * 100m;

                    // If they have less than 50% completion, it's a risk (we don't know the course progress, but it's an indicator)
                    if (completionRate < 50)
                    {
                        factors.Add($"Taxa de conclusao de atividades baixa ({completionRate:F1}%).");
                        riskLevel = riskLevel < RiskLevel.Alto ? RiskLevel.Medio : riskLevel;
                    }
                }
            }
            catch (Exception)
            {
                // Completion tracking might be disabled
                completionFailureCount++;
            }

            // Calculate overall risk
            if (factors.Count >= 3)
            {
                riskLevel = RiskLevel.Alto; // Aggravated risk due to multiple factors
            }

            if (riskLevel > RiskLevel.Baixo || factors.Count > 0)
            {
                reports.Add(new StudentRiskReport(
                    StudentId: student.UserId,
                    FullName: student.FullName,
                    RiskLevel: riskLevel,
                    Factors: factors,
                    LastCourseAccessAt: student.LastCourseAccessAt,
                    CurrentGrade: currentGrade,
                    CompletionRate: completionRate));
            }
        }

        // Return ordered by RiskLevel descending, then by inactivity
        var orderedReports = reports
            .OrderByDescending(r => r.RiskLevel)
            .ThenBy(r => r.LastCourseAccessAt ?? DateTimeOffset.MinValue)
            .ToList();

        return new StudentsAtRiskReportResult(
            orderedReports,
            participantsPage.Participants.Count,
            participantsPage.ClassificationDiagnostics ?? ParticipantClassificationDiagnostics.Empty,
            gradebookFailureCount,
            completionFailureCount);
    }
}
