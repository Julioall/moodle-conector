using System.Text.Json.Serialization;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tools;

public sealed record ActionConfirmationResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("riskLevel")] ToolRiskLevel RiskLevel,
    [property: JsonPropertyName("confirmedAt")] DateTimeOffset ConfirmedAt,
    [property: JsonPropertyName("auditId")] string? AuditId);
