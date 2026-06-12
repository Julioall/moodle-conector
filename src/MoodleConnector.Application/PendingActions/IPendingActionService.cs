using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.PendingActions;

public interface IPendingActionService
{
    Task<PendingActionResponse> CreatePendingActionAsync(
        string toolName,
        ToolRiskLevel riskLevel,
        object payload,
        object preview,
        string confirmationText,
        TimeSpan expiresIn,
        long? courseId,
        CancellationToken cancellationToken);
}
