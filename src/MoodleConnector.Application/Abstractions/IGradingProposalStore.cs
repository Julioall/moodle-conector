using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Armazena propostas de correção versionadas sem substituir versões anteriores.
/// A implementação deve ser idempotente para o mesmo item/versão/hash.
/// </summary>
public interface IGradingProposalStore
{
    Task<int> GetNextVersionAsync(Guid gradingItemId, CancellationToken cancellationToken);

    Task PublishAsync(AiGradingProposal proposal, CancellationToken cancellationToken);
}
