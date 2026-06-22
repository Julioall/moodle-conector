using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed partial class MoodleForumGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleForumGateway
{
    private static readonly string[] DiscussionSortFields = ["id", "timemodified", "timestart", "timeend"];
    private static readonly string[] PostSortFields = ["id", "created", "modified"];
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<IReadOnlyList<ForumDiscussionSummary>> GetForumDiscussionsPaginatedAsync(
        string userExternalId,
        string forumId,
        string sortBy,
        string sortDirection,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var normalizedForumId = ParseMoodleId(forumId, "forumId");
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await ResolveReadTokenAsync(cancellationToken);

        var endpoint = BuildMoodleGetUrl(
            credentials.BaseUrl,
            token,
            "mod_forum_get_forum_discussions_paginated",
            new Dictionary<string, string>
            {
                ["forumid"] = normalizedForumId.ToString(CultureInfo.InvariantCulture),
                ["sortby"] = NormalizeSortField(sortBy, DiscussionSortFields, "timemodified"),
                ["sortdirection"] = NormalizeSortDirection(sortDirection),
                ["page"] = Math.Max(0, page - 1).ToString(CultureInfo.InvariantCulture),
                ["perpage"] = Math.Max(1, pageSize).ToString(CultureInfo.InvariantCulture)
            });

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ForumDiscussionsResponseDto>(cancellationToken: cancellationToken);
        return (payload?.Discussions ?? [])
            .Select(ToDiscussion)
            .Where(discussion => !string.IsNullOrWhiteSpace(discussion.DiscussionId))
            .ToArray();
    }

    public async Task<IReadOnlyList<ForumPostSummary>> GetDiscussionPostsAsync(
        string userExternalId,
        string discussionId,
        string sortBy,
        string sortDirection,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var normalizedDiscussionId = ParseMoodleId(discussionId, "discussionId");
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await ResolveReadTokenAsync(cancellationToken);

        var endpoint = BuildMoodleGetUrl(
            credentials.BaseUrl,
            token,
            "mod_forum_get_discussion_posts",
            new Dictionary<string, string>
            {
                ["discussionid"] = normalizedDiscussionId.ToString(CultureInfo.InvariantCulture),
                ["sortby"] = NormalizeSortField(sortBy, PostSortFields, "created"),
                ["sortdirection"] = NormalizeSortDirection(sortDirection)
            });

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DiscussionPostsResponseDto>(cancellationToken: cancellationToken);
        return (payload?.Posts ?? [])
            .Select(ToPost)
            .Where(post => !string.IsNullOrWhiteSpace(post.PostId))
            .ToArray();
    }

    private static ForumDiscussionSummary ToDiscussion(JsonElement dto)
    {
        var discussionId = GetIdString(dto, "discussion");
        var firstPostId = GetIdString(dto, "id");
        if (string.IsNullOrWhiteSpace(discussionId))
        {
            discussionId = firstPostId;
        }

        return new ForumDiscussionSummary(
            discussionId,
            firstPostId,
            GetString(dto, "name") ?? GetString(dto, "subject") ?? string.Empty,
            GetString(dto, "subject") ?? GetString(dto, "name") ?? string.Empty,
            ToPlainText(GetString(dto, "message")),
            GetOptionalIdString(dto, "userid"),
            GetString(dto, "userfullname") ?? GetString(dto, "usermodifiedfullname"),
            ToDateTimeOffset(dto, "created") ?? ToDateTimeOffset(dto, "timecreated"),
            ToDateTimeOffset(dto, "modified") ?? ToDateTimeOffset(dto, "timemodified"),
            ToDateTimeOffset(dto, "timemodified"),
            GetNullableInt(dto, "numreplies") ?? GetNullableInt(dto, "replies") ?? 0,
            GetNullableInt(dto, "numunread") ?? 0,
            GetBool(dto, "pinned"),
            GetBool(dto, "locked"),
            GetBool(dto, "canreply"),
            PostsReturned: 0,
            PostsTotal: 0,
            Posts: []);
    }

    private static ForumPostSummary ToPost(JsonElement dto)
    {
        var discussionId = GetIdString(dto, "discussionid");
        if (string.IsNullOrWhiteSpace(discussionId))
        {
            discussionId = GetIdString(dto, "discussion");
        }

        return new ForumPostSummary(
            GetIdString(dto, "id"),
            discussionId,
            GetOptionalIdString(dto, "parentid") ?? GetOptionalIdString(dto, "parent"),
            GetOptionalIdString(dto, "userid") ?? GetNestedOptionalIdString(dto, "author", "id"),
            GetString(dto, "userfullname") ?? GetNestedString(dto, "author", "fullname"),
            GetString(dto, "subject") ?? GetString(dto, "replysubject") ?? string.Empty,
            ToPlainText(GetString(dto, "message")),
            ToDateTimeOffset(dto, "created") ?? ToDateTimeOffset(dto, "timecreated"),
            ToDateTimeOffset(dto, "modified") ?? ToDateTimeOffset(dto, "timemodified"),
            GetBool(dto, "deleted"),
            GetBool(dto, "canreply"),
            GetBool(dto, "postread") ?? GetBool(dto, "read"),
            GetBool(dto, "isprivatereply"),
            GetChildren(dto),
            GetAttachments(dto));
    }

    private static IReadOnlyList<string> GetChildren(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return children
            .EnumerateArray()
            .Select(ToIdString)
            .Where(child => !string.IsNullOrWhiteSpace(child))
            .ToArray();
    }

    private static IReadOnlyList<ForumAttachmentSummary> GetAttachments(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("attachments", out var attachments) ||
            attachments.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return attachments
            .EnumerateArray()
            .Where(attachment => attachment.ValueKind == JsonValueKind.Object)
            .Select(attachment => new ForumAttachmentSummary(
                GetString(attachment, "filename"),
                GetString(attachment, "filepath"),
                GetString(attachment, "mimetype"),
                GetNullableLong(attachment, "filesize"),
                MoodleContentUrlSanitizer.Sanitize(GetString(attachment, "fileurl")),
                GetBool(attachment, "isexternalfile")))
            .ToArray();
    }

    private static string BuildMoodleGetUrl(string baseUrl, string token, string wsFunction, IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new StringBuilder(baseUrl.TrimEnd('/')).Append("/webservice/rest/server.php?");
        builder.Append("wstoken=").Append(Uri.EscapeDataString(token));
        builder.Append("&wsfunction=").Append(Uri.EscapeDataString(wsFunction));
        builder.Append("&moodlewsrestformat=json");

        foreach (var pair in parameters)
        {
            builder.Append('&')
                .Append(Uri.EscapeDataString(pair.Key))
                .Append('=')
                .Append(Uri.EscapeDataString(pair.Value));
        }

        return builder.ToString();
    }

    private static int ParseMoodleId(string value, string parameterName)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

    private static string NormalizeSortField(string sortBy, IReadOnlyCollection<string> allowedFields, string defaultField)
    {
        var normalized = string.IsNullOrWhiteSpace(sortBy)
            ? defaultField
            : sortBy.Trim().ToLowerInvariant();

        return allowedFields.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : defaultField;
    }

    private static string NormalizeSortDirection(string sortDirection)
    {
        return string.Equals(sortDirection?.Trim(), "ASC", StringComparison.OrdinalIgnoreCase)
            ? "ASC"
            : "DESC";
    }

    private static string ToIdString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string GetIdString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property)
                ? ToIdString(property)
                : string.Empty;
    }

    private static string? GetOptionalIdString(JsonElement element, string propertyName)
    {
        var id = GetIdString(element, propertyName);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static string? GetNestedOptionalIdString(JsonElement element, string objectName, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(objectName, out var nested) ||
            nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = GetIdString(nested, propertyName);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.ToString(),
            _ => null
        };
    }

    private static string? GetNestedString(JsonElement element, string objectName, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(objectName, out var nested) ||
            nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(nested, propertyName);
    }

    private static int? GetNullableInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static long? GetNullableLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when property.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var boolean) => boolean,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number != 0,
            _ => null
        };
    }

    private static DateTimeOffset? ToDateTimeOffset(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        var seconds = property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0
        };

        return seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    private static string? ToPlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var withoutScript = ScriptTagRegex().Replace(value, " ");
        var withoutStyle = StyleTagRegex().Replace(withoutScript, " ");
        var withoutTags = HtmlTagRegex().Replace(withoutStyle, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalized = WhitespaceRegex().Replace(decoded, " ").Trim();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private async Task<string> ResolveReadTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.AllowServiceTokenForReadOnlyQueries && !string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            return _options.ServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
    }

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    private sealed class ForumDiscussionsResponseDto
    {
        [JsonPropertyName("discussions")]
        public IReadOnlyList<JsonElement>? Discussions { get; init; }
    }

    private sealed class DiscussionPostsResponseDto
    {
        [JsonPropertyName("posts")]
        public IReadOnlyList<JsonElement>? Posts { get; init; }
    }
}
