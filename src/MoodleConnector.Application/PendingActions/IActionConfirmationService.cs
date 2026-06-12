using MoodleConnector.Application.Tools;

namespace MoodleConnector.Application.PendingActions;

public interface IActionConfirmationService
{
    Task<ActionConfirmationResponse> ConfirmAsync(
        Guid pendingActionId,
        string confirmationText,
        string? requiredScope,
        CancellationToken cancellationToken);
}
