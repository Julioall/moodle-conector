using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public sealed record PendingActionConfirmationClaimResult(
    bool ConfirmedByCaller,
    PendingActionStatus Status,
    DateTimeOffset? ConfirmedAt);

public sealed record PendingActionReconciliationClaimResult(
    bool ResolvedByCaller,
    PendingActionStatus Status,
    string? AuditId);

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

    /// <summary>
    /// Atomically resolves an execution-unknown action and records the
    /// operator decision. Implementations backed by a database must claim the
    /// row with a conditional update so two operators cannot resolve it twice.
    /// </summary>
    Task<PendingActionReconciliationClaimResult> TryResolveExecutionUnknownWithAuditAsync(
        Guid id,
        PendingActionStatus resolvedStatus,
        MoodleAuditLog reconciliationAudit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("O repositorio não oferece reconciliação atômica.");

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
