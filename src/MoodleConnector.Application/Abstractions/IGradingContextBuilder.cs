using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Monta o contexto mínimo de correção para um item de lote a partir
/// dos dados disponíveis localmente e de gateways configurados.
/// </summary>
public interface IGradingContextBuilder
{
    /// <summary>
    /// Monta o contexto de correção para o item dado.
    /// Retorna objeto com bloqueadores quando dados mínimos não estão disponíveis.
    /// </summary>
    Task<GradingContext> BuildAsync(
        AssistedGradingItem item,
        GradingContextOptions options,
        CancellationToken cancellationToken);
}

public sealed record GradingContextOptions(
    bool IncludeRubric = true,
    bool IncludeSubmissionFiles = true,
    bool IncludeCourseMaterials = false,
    string? TeacherInstructions = null);
