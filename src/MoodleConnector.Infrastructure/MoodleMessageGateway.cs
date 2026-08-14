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
    IMoodleRestClient restClient,
    IMoodleCurrentUserIdGateway currentUserIdGateway) : IMoodleMessageGateway
{
    private const string SendMessagesFunction = "core_message_send_instant_messages";
    private const string GetConversationsFunction = "core_message_get_conversations";
    private const string GetConversationBetweenUsersFunction = "core_message_get_conversation_between_users";
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<MoodleConversationsResult> GetConversationsAsync(CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            return new MoodleConversationsResult(0, []);
        }

        var currentUserId = await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken);
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var payload = await restClient.CallAsync(
            credentials,
            GetConversationsFunction,
            new Dictionary<string, object?>
            {
                ["userid"] = currentUserId.ToString(CultureInfo.InvariantCulture),
                ["type"] = "1",
                ["limitnum"] = "50"
            },
            allowServiceToken: true,
            cancellationToken);

        var conversations = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("conversations", out var items)
            ? items.EnumerateArray()
                .Select(item => ParseConversation(item, currentUserId))
                .Where(item => item is not null)
                .Cast<MoodleConversationSummary>()
                .ToArray()
            : Array.Empty<MoodleConversationSummary>();

        return new MoodleConversationsResult(currentUserId, conversations);
    }

    public async Task<MoodleMessagesResult> GetMessagesAsync(
        long otherUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (otherUserId <= 0) throw new ArgumentOutOfRangeException(nameof(otherUserId));
        if (_options.UseStubData)
        {
            return new MoodleMessagesResult(0, null, []);
        }

        var currentUserId = await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken);
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var payload = await restClient.CallAsync(
            credentials,
            GetConversationBetweenUsersFunction,
            new Dictionary<string, object?>
            {
                ["userid"] = currentUserId.ToString(CultureInfo.InvariantCulture),
                ["otheruserid"] = otherUserId.ToString(CultureInfo.InvariantCulture),
                ["includecontactrequests"] = "0",
                ["includeprivacyinfo"] = "0",
                ["messagelimit"] = Math.Clamp(limit, 1, 100).ToString(CultureInfo.InvariantCulture),
                ["messageoffset"] = "0",
                ["newestmessagesfirst"] = "1"
            },
            allowServiceToken: true,
            cancellationToken);

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new MoodleMessagesResult(currentUserId, null, []);
        }

        var conversationId = ReadLong(payload, "id");
        var messages = payload.TryGetProperty("messages", out var rawMessages) && rawMessages.ValueKind == JsonValueKind.Array
            ? rawMessages.EnumerateArray()
                .Select(item => ParseMessage(item, currentUserId))
                .Where(item => item is not null)
                .Cast<MoodleConversationMessage>()
                .OrderBy(item => item.CreatedAtUnix)
                .ToArray()
            : Array.Empty<MoodleConversationMessage>();

        return new MoodleMessagesResult(currentUserId, conversationId, messages);
    }

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
            // The Claris composer sends plain text. Keeping the Moodle payload
            // plain also prevents user-entered markup from being interpreted.
            formParams[$"messages[{i}][textformat]"] = "0";
        }

        var payload = await restClient.CallAsync(credentials, SendMessagesFunction, formParams, allowServiceToken: false, cancellationToken);
        return ParseSendResult(payload.GetRawText(), recipientUserIds);
    }

    private static MoodleConversationSummary? ParseConversation(JsonElement value, long currentUserId)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var conversationId = ReadLong(value, "id");
        if (conversationId is null) return null;

        var members = value.TryGetProperty("members", out var rawMembers) && rawMembers.ValueKind == JsonValueKind.Array
            ? rawMembers.EnumerateArray().Select(ParseMember).Where(item => item is not null).Cast<MoodleMessageMember>().ToArray()
            : Array.Empty<MoodleMessageMember>();
        var member = members.FirstOrDefault(item => item.Id != currentUserId) ?? members.FirstOrDefault();
        if (member is null) return null;

        MoodleConversationLastMessage? lastMessage = null;
        if (value.TryGetProperty("messages", out var rawMessages) && rawMessages.ValueKind == JsonValueKind.Array)
        {
            lastMessage = rawMessages.EnumerateArray()
                .Select(ParseLastMessage)
                .Where(item => item is not null)
                .Cast<MoodleConversationLastMessage>()
                .FirstOrDefault();
        }

        var unreadCount = ReadLong(value, "unreadcount") ?? 0;
        return new MoodleConversationSummary(
            conversationId.Value,
            member,
            lastMessage,
            (int)Math.Clamp(unreadCount, 0, int.MaxValue),
            StudentId: null);
    }

    private static MoodleMessageMember? ParseMember(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var id = ReadLong(value, "id");
        if (id is null) return null;
        return new MoodleMessageMember(
            id.Value,
            ReadString(value, "fullname") ?? "Desconhecido",
            ReadString(value, "profileimageurl"));
    }

    private static MoodleConversationLastMessage? ParseLastMessage(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        return new MoodleConversationLastMessage(
            ReadString(value, "text") ?? string.Empty,
            ReadLong(value, "timecreated") ?? 0);
    }

    private static MoodleConversationMessage? ParseMessage(JsonElement value, long currentUserId)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var id = ReadString(value, "id") ?? ReadLong(value, "id")?.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(id)) return null;
        var senderId = ReadLong(value, "useridfrom") ?? 0;
        return new MoodleConversationMessage(
            id,
            ReadString(value, "text") ?? string.Empty,
            ReadLong(value, "timecreated") ?? 0,
            senderId,
            senderId == currentUserId ? "tutor" : "student");
    }

    private static long? ReadLong(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number)) return number;
        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static string? ReadString(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property)) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
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
