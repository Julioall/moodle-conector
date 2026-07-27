using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public sealed record PendingActionConfirmationClaimResult(
    bool ConfirmedByCaller,
    PendingActionStatus Status,
    DateTimeOffset? ConfirmedAt);

public interface IPendingMoodleActionRepository
{
    Task AddAsync(PendingMoodleAction action, CancellationToken cancellationToken);

    Task<PendingMoodleAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PendingActionConfirmationClaimResult> TryConfirmWithAuditAsync(
        Guid id,
        string confirmedBySubject,
        DateTimeOffset confirmedAt,
        MoodleAuditLog confirmationAudit,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
