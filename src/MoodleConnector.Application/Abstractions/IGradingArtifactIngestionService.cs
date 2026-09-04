using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Completa as referências de um item de correção fora do request HTTP.
/// A entrega dos arquivos ocorre diretamente pelo MCP Resource no chat; este
/// contrato não possui operação de download ou extração local.
/// </summary>
public interface IGradingArtifactIngestionService
{
    Task IngestPendingAsync(
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        CancellationToken cancellationToken);

}
