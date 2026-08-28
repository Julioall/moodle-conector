namespace MoodleConnector.Domain;

public sealed class PendingMoodleAction
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string ToolName { get; init; } = string.Empty;

    public ToolRiskLevel RiskLevel { get; init; }

    public string CreatedBySubject { get; init; } = string.Empty;

    public string? CreatedByEmail { get; init; }

    public long? CreatedByMoodleUserId { get; init; }

    public long? CourseId { get; init; }

    public string PayloadJson { get; init; } = "{}";

    public string PreviewJson { get; init; } = "{}";

    public string ConfirmationText { get; init; } = string.Empty;

    public PendingActionStatus Status { get; private set; } = PendingActionStatus.PendingConfirmation;

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public string? ConfirmedBySubject { get; private set; }

    public string IdempotencyKey { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public void MarkExpired()
    {
        if (Status == PendingActionStatus.PendingConfirmation)
        {
            Status = PendingActionStatus.Expired;
        }
    }

    public void Confirm(string confirmedBySubject, DateTimeOffset confirmedAt)
    {
        ConfirmedBySubject = confirmedBySubject;
        ConfirmedAt = confirmedAt;
        Status = PendingActionStatus.Confirmed;
    }

    public void MarkExecutionUnknown()
    {
        // A confirmação é persistida com uma atualização atômica. O agregado
        // que iniciou o fluxo pode continuar com o snapshot
        // PendingConfirmation no mesmo DbContext; neste ponto, contudo, a
        // confirmação já foi validada antes de qualquer escrita remota.
        if (Status is PendingActionStatus.Confirmed or PendingActionStatus.PendingConfirmation)
        {
            Status = PendingActionStatus.ExecutionUnknown;
        }
    }

    public void ResolveExecutionUnknown(PendingActionStatus resolvedStatus)
    {
        if (Status != PendingActionStatus.ExecutionUnknown)
        {
            throw new InvalidOperationException($"A ação não está em execução desconhecida: {Status}.");
        }

        if (resolvedStatus is not (PendingActionStatus.Executed or PendingActionStatus.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(resolvedStatus), "A reconciliação deve resolver para Executed ou Failed.");
        }

        Status = resolvedStatus;
    }
}
