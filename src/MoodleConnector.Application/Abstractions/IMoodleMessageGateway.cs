namespace MoodleConnector.Application.Abstractions;

public sealed record MessageSendResult(
    bool Success,
    int SentCount,
    int FailedCount,
    IReadOnlyList<string> FailedUserIds,
    string? ErrorMessage);

public interface IMoodleMessageGateway
{
    /// <summary>
    /// Sends an instant message to one or more Moodle users.
    /// </summary>
    Task<MessageSendResult> SendMessagesToUsersAsync(
        string senderExternalId,
        IReadOnlyList<string> recipientUserIds,
        string messageText,
        CancellationToken cancellationToken);
}
