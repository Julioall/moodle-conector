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
        var belowMinimum = item.PercentageFormatted.HasValue
            && item.PercentageFormatted.Value < minGradePercent;

        return new StudentGradeItem(
            ItemId: item.Id,
            ItemName: item.ItemName,
            ItemType: item.ItemType,
            ItemModule: item.ItemModule,
            GradeRaw: item.GradeRaw,
            GradeMax: item.GradeMax,
            PercentageFormatted: item.PercentageFormatted,
            BelowMinimum: belowMinimum,
            Feedback: item.Feedback);
    }

    /// <summary>
    /// Retorna apenas os itens que representam atividades (exclui course e category).
    /// </summary>
    internal static bool IsActivityItem(GradebookItem item) =>
        !string.Equals(item.ItemType, "course", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(item.ItemType, "category", StringComparison.OrdinalIgnoreCase);
}
