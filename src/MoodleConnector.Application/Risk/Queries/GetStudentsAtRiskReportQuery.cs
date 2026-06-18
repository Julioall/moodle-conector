using MediatR;

namespace MoodleConnector.Application.Risk.Queries;

public enum RiskLevel
{
    Baixo = 0,
    Medio = 1,
    Alto = 2
}

public sealed record StudentRiskReport(
    string StudentId,
    string FullName,
    RiskLevel RiskLevel,
    IReadOnlyList<string> Factors,
    DateTimeOffset? LastCourseAccessAt,
    decimal? CurrentGrade,
    decimal? CompletionRate);

public sealed record GetStudentsAtRiskReportQuery(
    string CourseId,
    int MaxStudentsToAnalyze,
    int InactivityThresholdDays = 7,
    decimal MinGradePercentage = 60m) : IRequest<IReadOnlyList<StudentRiskReport>>;
