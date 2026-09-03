using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Completa a ingestão pesada de um item de correção fora do request HTTP.
/// O serviço é deliberadamente idempotente: referências já persistidas não são
/// recriadas e cada artifact é atualizado antes de o item avançar para análise.
/// </summary>
public interface IGradingArtifactIngestionService
{
    Task IngestPendingAsync(
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        CancellationToken cancellationToken);

    /// <summary>
    /// Materializa apenas os anexos da submissão quando o caminho MCP não pode
    /// ser usado. Não deve ser chamado no caminho normal de MCP Resources.
    /// </summary>
    Task MaterializeLegacySubmissionFallbackAsync(
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        CancellationToken cancellationToken);
}
