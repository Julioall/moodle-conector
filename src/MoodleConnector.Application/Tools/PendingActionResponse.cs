using System.Text.Json.Serialization;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tools;

public sealed record PendingActionResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("riskLevel")] ToolRiskLevel RiskLevel,
    [property: JsonPropertyName("preview")] object Preview,
    [property: JsonPropertyName("confirmationText")] string ConfirmationText,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);
