using System.Text.Json.Serialization;

namespace MoodleConnector.Application.MoodleApi;

public sealed record MoodleWriteReconciliationResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
    [property: JsonPropertyName("function")] string Function,
    [property: JsonPropertyName("resolution")] string Resolution,
    [property: JsonPropertyName("auditId")] string? AuditId,
    [property: JsonPropertyName("message")] string Message);

public interface IMoodleWriteReconciliationService
{
    Task<MoodleWriteReconciliationResult> ReconcileAsync(
        Guid pendingActionId,
        string resolution,
        CancellationToken cancellationToken);
}
