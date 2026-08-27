using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Armazenamento append-only da identidade e do payload operacional do contexto.
/// A implementação deve ser idempotente para a mesma combinação item/versão/hash.
/// </summary>
public interface IGradingContextSnapshotStore
{
    Task PublishAsync(
        GradingContextSnapshot snapshot,
        CancellationToken cancellationToken);
}
