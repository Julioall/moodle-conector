using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Gradebook.Queries;

/// <summary>
/// Mapeamento compartilhado entre as queries de gradebook do ciclo semanal do tutor.
/// </summary>
internal static class GradebookMappingHelper
{
    /// <summary>
    /// Mapeia um <see cref="GradebookItem"/> para <see cref="StudentGradeItem"/>,
    /// aplicando a flag <see cref="StudentGradeItem.BelowMinimum"/> conforme o threshold informado.
    /// </summary>
    internal static StudentGradeItem ToStudentGradeItem(GradebookItem item, decimal minGradePercent)
    {
        var percentage = ResolvePercentage(item);
        var belowMinimum = percentage.HasValue && percentage.Value < minGradePercent;

        return new StudentGradeItem(
            ItemId: item.Id,
            ItemName: item.ItemName,
            ItemType: item.ItemType,
            ItemModule: item.ItemModule,
            GradeRaw: item.GradeRaw,
            GradeMax: item.GradeMax,
            PercentageFormatted: percentage,
            BelowMinimum: belowMinimum,
            Feedback: item.Feedback);
    }

    /// <summary>
    /// Uses Moodle's percentage when available and derives it from the raw grade
    /// when Moodle omits the field (a common shape for zero grades).
    /// </summary>
    internal static decimal? ResolvePercentage(GradebookItem item)
    {
        if (item.PercentageFormatted.HasValue)
        {
            return item.PercentageFormatted;
        }

        var gradeMin = item.GradeMin ?? 0m;
        var gradeMax = item.GradeMax;
        if (!item.GradeRaw.HasValue || !gradeMax.HasValue || gradeMax.Value <= gradeMin)
        {
            return null;
        }

        return Math.Round(
            (item.GradeRaw.Value - gradeMin) / (gradeMax.Value - gradeMin) * 100m,
            2,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Retorna apenas os itens que representam atividades (exclui course e category).
    /// </summary>
    internal static bool IsActivityItem(GradebookItem item) =>
        !string.Equals(item.ItemType, "course", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(item.ItemType, "category", StringComparison.OrdinalIgnoreCase);

    internal static bool IsCourseTotalItem(GradebookItem item) =>
        string.Equals(item.ItemType, "course", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moodle does not expose an "optional" flag in the grade item payload.
    /// In this connector's course convention, recovery activities are named
    /// accordingly and must not become pending for every student.
    /// </summary>
    internal static bool IsOptionalRecoveryItem(GradebookItem item) =>
        IsActivityItem(item) &&
        item.ItemName.Contains("recupera", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDerivedReportItem(GradebookItem item) =>
        !string.Equals(item.ItemType, "category", StringComparison.OrdinalIgnoreCase) &&
        !IsOptionalRecoveryItem(item);

    internal static bool IsDerivedReportActivityItem(GradebookItem item) =>
        IsActivityItem(item) && !IsOptionalRecoveryItem(item);
}
