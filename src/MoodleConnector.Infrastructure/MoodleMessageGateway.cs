using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

/// <summary>
/// Implementação do gateway de mensagens instantâneas via API Moodle (core_message_send_instant_messages).
/// </summary>
internal sealed class MoodleMessageGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleMessageGateway
{
    private const string SendMessagesFunction = "core_message_send_instant_messages";
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<MessageSendResult> SendMessagesToUsersAsync(
        string senderExternalId,
        IReadOnlyList<string> recipientUserIds,
        string messageText,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para escritas Moodle reais.");
        }

        if (recipientUserIds == null || recipientUserIds.Count == 0)
        {
            return new MessageSendResult(true, 0, 0, [], null);
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        if (!credentials.CanWrite)
        {
            throw new InvalidOperationException("A conexao Moodle atual nao permite escrita.");
        }

        var normalizedRecipientIds = new List<string>(recipientUserIds.Count);
        foreach (var recipientId in recipientUserIds)
        {
            if (!long.TryParse(recipientId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            {
                return new MessageSendResult(
                    Success: false,
                    SentCount: 0,
                    FailedCount: recipientUserIds.Count,
                    FailedUserIds: recipientUserIds.ToArray(),
                    ErrorMessage: "A lista de destinatários contém identificadores Moodle inválidos.");
            }

            normalizedRecipientIds.Add(userId.ToString(CultureInfo.InvariantCulture));
        }

        var formParams = new Dictionary<string, object?>();

        for (int i = 0; i < normalizedRecipientIds.Count; i++)
        {
            formParams[$"messages[{i}][touserid]"] = normalizedRecipientIds[i];
            formParams[$"messages[{i}][text]"] = messageText;
            formParams[$"messages[{i}][textformat]"] = "1"; // HTML
        }

        var payload = await restClient.CallAsync(credentials, SendMessagesFunction, formParams, allowServiceToken: false, cancellationToken);
        return ParseSendResult(payload.GetRawText(), recipientUserIds);
    }

    private MessageSendResult ParseSendResult(string payload, IReadOnlyList<string> requested)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            string.Equals(payload.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            // Empty response ? assume all sent (some Moodle versions return null on success)
            return new MessageSendResult(true, requested.Count, 0, [], null);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        // Check for top-level error
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("exception", out _))
        {
            var errorCode = root.TryGetProperty("errorcode", out var errEl) ? errEl.GetString() : "moodle_error";
            return new MessageSendResult(false, 0, requested.Count, requested.ToList(), errorCode);
        }

        // Response is an array of message results
        if (root.ValueKind != JsonValueKind.Array)
        {
            return new MessageSendResult(true, requested.Count, 0, [], null);
        }

        var failed = new List<string>();
        var sent = 0;

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var msgUserId = item.TryGetProperty("msgid", out var msgIdEl) ? msgIdEl.GetInt64() : 0L;
            var clientMsgId = item.TryGetProperty("clientmsgid", out var clientEl) ? clientEl.GetString() : null;

            if (msgUserId < 0)
            {
                // Moodle returns msgid = -1 for failed deliveries
                // clientmsgid is the index in the request
                if (!string.IsNullOrEmpty(clientMsgId) &&
                    int.TryParse(clientMsgId, out var idx) &&
                    idx >= 0 && idx < requested.Count)
                {
                    failed.Add(requested[idx]);
                }
                else
                {
                    failed.Add("unknown");
                }
            }
            else
            {
                sent++;
            }
        }

        if (sent == 0 && failed.Count == 0)
        {
            // Couldn't parse - assume success
            return new MessageSendResult(true, requested.Count, 0, [], null);
        }

        return new MessageSendResult(
            Success: failed.Count == 0,
            SentCount: sent,
            FailedCount: failed.Count,
            FailedUserIds: failed,
            ErrorMessage: failed.Count > 0 ? $"Falha ao entregar mensagem para {failed.Count} destinatário(s)." : null);
    }

}
