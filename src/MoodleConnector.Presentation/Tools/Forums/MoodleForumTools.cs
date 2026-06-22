using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Forums;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleForumTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    private static readonly string[] AllowedSortBy = ["id", "timemodified", "timestart", "timeend"];

    [McpServerTool(
        Name = "ler_forum",
        Title = "Ler Forum",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ReadForumResponse>))]
    [Description("Le discussoes e posts de um forum Moodle usando mod_forum_get_forum_discussions_paginated e mod_forum_get_discussion_posts.")]
    public Task<CallToolResult> LerForumAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Identificador do forum. Pode ser cmid ou instance id.")]
        string forumId,
        [Description("Pagina de discussoes, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina de discussoes, de 1 a 25.")]
        int tamanhoPagina = 10,
        [Description("Quando true, carrega posts de cada discussao via mod_forum_get_discussion_posts.")]
        bool incluirPosts = true,
        [Description("Quantidade maxima de posts retornados por discussao, de 1 a 100.")]
        int postsPorDiscussao = 50,
        [Description("Campo de ordenacao das discussoes: id, timemodified, timestart ou timeend.")]
        string ordenarPor = "timemodified",
        [Description("Direcao da ordenacao das discussoes: ASC ou DESC.")]
        string ordem = "DESC",
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ReadForumCoreAsync(
            courseId,
            forumId,
            pagina,
            tamanhoPagina,
            incluirPosts,
            postsPorDiscussao,
            ordenarPor,
            ordem,
            moodleAlias,
            cancellationToken,
            language: "pt");
    }

    [McpServerTool(
        Name = "read_forum",
        Title = "Read Forum",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ReadForumResponse>))]
    [Description("Reads Moodle forum discussions and posts using mod_forum_get_forum_discussions_paginated and mod_forum_get_discussion_posts.")]
    public Task<CallToolResult> ReadForumAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("Forum identifier. Can be cmid or instance id.")]
        string forumId,
        [Description("Discussion result page, starting at 1.")]
        int page = 1,
        [Description("Discussion page size, from 1 to 25.")]
        int pageSize = 10,
        [Description("When true, loads posts for each discussion through mod_forum_get_discussion_posts.")]
        bool includePosts = true,
        [Description("Maximum posts returned per discussion, from 1 to 100.")]
        int postsPerDiscussion = 50,
        [Description("Discussion sort field: id, timemodified, timestart, or timeend.")]
        string sortBy = "timemodified",
        [Description("Discussion sort direction: ASC or DESC.")]
        string sortDirection = "DESC",
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ReadForumCoreAsync(
            courseId,
            forumId,
            page,
            pageSize,
            includePosts,
            postsPerDiscussion,
            sortBy,
            sortDirection,
            moodleAlias,
            cancellationToken,
            language: "en");
    }

    private async Task<CallToolResult> ReadForumCoreAsync(
        string courseId,
        string forumId,
        int page,
        int pageSize,
        bool includePosts,
        int postsPerDiscussion,
        string sortBy,
        string sortDirection,
        string? moodleAlias,
        CancellationToken cancellationToken,
        string language)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return Error<ReadForumResponse>(language == "pt" ? "Informe um identificador de curso." : "Provide a course identifier.");
        }

        if (string.IsNullOrWhiteSpace(forumId))
        {
            return Error<ReadForumResponse>(language == "pt" ? "Informe um identificador de forum." : "Provide a forum identifier.");
        }

        if (!TryNormalizeSortBy(sortBy, out var normalizedSortBy))
        {
            return Error<ReadForumResponse>(
                language == "pt"
                    ? "Campo de ordenacao invalido. Use id, timemodified, timestart ou timeend."
                    : "Invalid sort field. Use id, timemodified, timestart, or timeend.");
        }

        if (!TryNormalizeSortDirection(sortDirection, out var normalizedSortDirection))
        {
            return Error<ReadForumResponse>(
                language == "pt"
                    ? "Direcao de ordenacao invalida. Use ASC ou DESC."
                    : "Invalid sort direction. Use ASC or DESC.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<ReadForumResponse>(
                language == "pt"
                    ? "Usuario nao autenticado para ler forum."
                    : "User is not authenticated to read forum.");
        }

        ForumReadPage? forumPage;
        try
        {
            forumPage = await mediator.Send(
                new ReadForumQuery(
                    moodleUserId.Value.ToString(),
                    courseId,
                    forumId,
                    page,
                    pageSize,
                    normalizedSortBy,
                    normalizedSortDirection,
                    includePosts,
                    postsPerDiscussion),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<ReadForumResponse>(
                language == "pt"
                    ? "Nao foi possivel ler o forum no Moodle neste momento."
                    : "Could not read the Moodle forum at this time.");
        }

        if (forumPage is null)
        {
            return Error<ReadForumResponse>(
                language == "pt"
                    ? "Curso ou forum nao encontrados entre os dados autorizados do usuario."
                    : "Course or forum was not found in the user's authorized data.");
        }

        var data = ToResponse(forumPage);
        var response = new ToolResponse<ReadForumResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildNarration(data, language) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static string BuildNarration(ReadForumResponse response, string language)
    {
        if (language == "pt")
        {
            return response.ReturnedCount == 0
                ? $"Nao encontrei discussoes no forum {response.ForumName} para a pagina informada."
                : $"Li {response.ReturnedCount} discussao(oes) do forum {response.ForumName}. Posts carregados: {response.IncludePosts}.";
        }

        return response.ReturnedCount == 0
            ? $"No discussions were found in forum {response.ForumName} for the requested page."
            : $"Read {response.ReturnedCount} discussion(s) from forum {response.ForumName}. Posts loaded: {response.IncludePosts}.";
    }

    private static ReadForumResponse ToResponse(ForumReadPage page)
    {
        return new ReadForumResponse(
            page.CourseId,
            page.ForumId,
            page.ForumModuleId,
            page.ForumName,
            page.Page,
            page.PageSize,
            page.SortBy,
            page.SortDirection,
            page.IncludePosts,
            page.PostsPerDiscussion,
            page.ReturnedCount,
            page.HasMore,
            page.Discussions.Select(ToDiscussionItem).ToArray());
    }

    private static ForumDiscussionItem ToDiscussionItem(ForumDiscussionSummary discussion)
    {
        return new ForumDiscussionItem(
            discussion.DiscussionId,
            discussion.FirstPostId,
            discussion.Name,
            discussion.Subject,
            discussion.MessageText,
            discussion.AuthorUserId,
            discussion.AuthorFullName,
            discussion.CreatedAt,
            discussion.ModifiedAt,
            discussion.LastModifiedAt,
            discussion.ReplyCount,
            discussion.UnreadCount,
            discussion.Pinned,
            discussion.Locked,
            discussion.CanReply,
            discussion.PostsReturned,
            discussion.PostsTotal,
            discussion.Posts.Select(ToPostItem).ToArray());
    }

    private static ForumPostItem ToPostItem(ForumPostSummary post)
    {
        return new ForumPostItem(
            post.PostId,
            post.DiscussionId,
            post.ParentPostId,
            post.UserId,
            post.UserFullName,
            post.Subject,
            post.MessageText,
            post.CreatedAt,
            post.ModifiedAt,
            post.Deleted,
            post.CanReply,
            post.PostRead,
            post.IsPrivateReply,
            post.Children,
            post.Attachments.Select(ToAttachmentItem).ToArray());
    }

    private static ForumAttachmentItem ToAttachmentItem(ForumAttachmentSummary attachment)
    {
        return new ForumAttachmentItem(
            attachment.FileName,
            attachment.FilePath,
            attachment.MimeType,
            attachment.SizeBytes,
            attachment.FileUrl,
            attachment.IsExternalFile);
    }

    private static bool TryNormalizeSortBy(string? sortBy, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(sortBy)
            ? "timemodified"
            : sortBy.Trim().ToLowerInvariant();

        return AllowedSortBy.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeSortDirection(string? sortDirection, out string normalized)
    {
        if (string.Equals(sortDirection?.Trim(), "ASC", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "ASC";
            return true;
        }

        if (string.IsNullOrWhiteSpace(sortDirection) ||
            string.Equals(sortDirection.Trim(), "DESC", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "DESC";
            return true;
        }

        normalized = "DESC";
        return false;
    }

    private static CallToolResult Error<T>(string message)
    {
        var response = new ToolResponse<T>(
            "error",
            Data: default,
            Warnings: [message],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }

    public sealed record ReadForumResponse(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("forumId")] string ForumId,
        [property: JsonPropertyName("forumModuleId")] string ForumModuleId,
        [property: JsonPropertyName("forumName")] string ForumName,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("sortBy")] string SortBy,
        [property: JsonPropertyName("sortDirection")] string SortDirection,
        [property: JsonPropertyName("includePosts")] bool IncludePosts,
        [property: JsonPropertyName("postsPerDiscussion")] int PostsPerDiscussion,
        [property: JsonPropertyName("returnedCount")] int ReturnedCount,
        [property: JsonPropertyName("hasMore")] bool HasMore,
        [property: JsonPropertyName("discussions")] IReadOnlyList<ForumDiscussionItem> Discussions);

    public sealed record ForumDiscussionItem(
        [property: JsonPropertyName("discussionId")] string DiscussionId,
        [property: JsonPropertyName("firstPostId")] string FirstPostId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("messageText")] string? MessageText,
        [property: JsonPropertyName("authorUserId")] string? AuthorUserId,
        [property: JsonPropertyName("authorFullName")] string? AuthorFullName,
        [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("modifiedAt")] DateTimeOffset? ModifiedAt,
        [property: JsonPropertyName("lastModifiedAt")] DateTimeOffset? LastModifiedAt,
        [property: JsonPropertyName("replyCount")] int ReplyCount,
        [property: JsonPropertyName("unreadCount")] int UnreadCount,
        [property: JsonPropertyName("pinned")] bool? Pinned,
        [property: JsonPropertyName("locked")] bool? Locked,
        [property: JsonPropertyName("canReply")] bool? CanReply,
        [property: JsonPropertyName("postsReturned")] int PostsReturned,
        [property: JsonPropertyName("postsTotal")] int PostsTotal,
        [property: JsonPropertyName("posts")] IReadOnlyList<ForumPostItem> Posts);

    public sealed record ForumPostItem(
        [property: JsonPropertyName("postId")] string PostId,
        [property: JsonPropertyName("discussionId")] string DiscussionId,
        [property: JsonPropertyName("parentPostId")] string? ParentPostId,
        [property: JsonPropertyName("userId")] string? UserId,
        [property: JsonPropertyName("userFullName")] string? UserFullName,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("messageText")] string? MessageText,
        [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("modifiedAt")] DateTimeOffset? ModifiedAt,
        [property: JsonPropertyName("deleted")] bool? Deleted,
        [property: JsonPropertyName("canReply")] bool? CanReply,
        [property: JsonPropertyName("postRead")] bool? PostRead,
        [property: JsonPropertyName("isPrivateReply")] bool? IsPrivateReply,
        [property: JsonPropertyName("children")] IReadOnlyList<string> Children,
        [property: JsonPropertyName("attachments")] IReadOnlyList<ForumAttachmentItem> Attachments);

    public sealed record ForumAttachmentItem(
        [property: JsonPropertyName("fileName")] string? FileName,
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("mimeType")] string? MimeType,
        [property: JsonPropertyName("sizeBytes")] long? SizeBytes,
        [property: JsonPropertyName("fileUrl")] string? FileUrl,
        [property: JsonPropertyName("isExternalFile")] bool? IsExternalFile);
}
