using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Forums;

public sealed record ReadForumQuery(
    string UserExternalId,
    string CourseId,
    string ForumId,
    int Page,
    int PageSize,
    string SortBy,
    string SortDirection,
    bool IncludePosts,
    int PostsPerDiscussion) : IRequest<ForumReadPage?>;

public sealed class ReadForumQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleForumGateway forumGateway)
    : IRequestHandler<ReadForumQuery, ForumReadPage?>
{
    private static readonly string[] ForumModuleTypes = ["forum"];

    public async Task<ForumReadPage?> Handle(
        ReadForumQuery request,
        CancellationToken cancellationToken)
    {
        var course = await coursesGateway.GetMyCourseAsync(
            request.UserExternalId,
            request.CourseId,
            cancellationToken);
        if (course is null)
        {
            return null;
        }

        var forum = await ResolveForumAsync(
            request.UserExternalId,
            course.CourseId,
            request.ForumId,
            cancellationToken);
        if (forum is null)
        {
            return null;
        }

        var page = NormalizePage(request.Page);
        var pageSize = NormalizePageSize(request.PageSize);
        var sortBy = NormalizeDiscussionSortBy(request.SortBy);
        var sortDirection = NormalizeSortDirection(request.SortDirection);
        var postsPerDiscussion = NormalizePostsPerDiscussion(request.PostsPerDiscussion);
        var forumInstanceId = forum.InstanceId ?? forum.ActivityId;

        var fetchedDiscussions = await forumGateway.GetForumDiscussionsPaginatedAsync(
            request.UserExternalId,
            forumInstanceId,
            sortBy,
            sortDirection,
            page,
            pageSize + 1,
            cancellationToken);

        // Fallback for single-type forums: when the standard discussions endpoint
        // returns empty, check the forum type via mod_forum_get_forums_by_courses.
        // Single-discussion forums often don't expose their discussion through
        // the listing endpoint; we fetch posts directly instead.
        if (fetchedDiscussions.Count == 0 && page == 1)
        {
            var singleDiscussions = await TryGetSingleForumDiscussionsAsync(
                request.UserExternalId,
                course.CourseId,
                forumInstanceId,
                forum.Name,
                request.IncludePosts,
                postsPerDiscussion,
                cancellationToken);
            if (singleDiscussions is not null)
            {
                return new ForumReadPage(
                    course.CourseId,
                    forumInstanceId,
                    forum.ActivityId,
                    forum.Name,
                    page,
                    pageSize,
                    sortBy,
                    sortDirection,
                    request.IncludePosts,
                    postsPerDiscussion,
                    singleDiscussions.Count,
                    HasMore: false,
                    singleDiscussions);
            }
        }

        var hasMore = fetchedDiscussions.Count > pageSize;
        var pageDiscussions = fetchedDiscussions.Take(pageSize).ToArray();
        var discussions = new List<ForumDiscussionSummary>(pageDiscussions.Length);
        foreach (var discussion in pageDiscussions)
        {
            if (!request.IncludePosts)
            {
                discussions.Add(discussion);
                continue;
            }

            var posts = await forumGateway.GetDiscussionPostsAsync(
                request.UserExternalId,
                discussion.DiscussionId,
                "created",
                "ASC",
                cancellationToken);
            var limitedPosts = posts.Take(postsPerDiscussion).ToArray();
            discussions.Add(discussion with
            {
                Posts = limitedPosts,
                PostsReturned = limitedPosts.Length,
                PostsTotal = posts.Count
            });
        }

        return new ForumReadPage(
            course.CourseId,
            forumInstanceId,
            forum.ActivityId,
            forum.Name,
            page,
            pageSize,
            sortBy,
            sortDirection,
            request.IncludePosts,
            postsPerDiscussion,
            discussions.Count,
            hasMore,
            discussions);
    }

    /// <summary>
    /// For single-type forums, the standard discussion listing endpoint may return
    /// empty results. This method detects that case via mod_forum_get_forums_by_courses
    /// and fetches posts directly using mod_forum_get_discussion_posts.
    /// Returns null if the forum is not of type 'single' or if no posts are found.
    /// </summary>
    private async Task<IReadOnlyList<ForumDiscussionSummary>?> TryGetSingleForumDiscussionsAsync(
        string userExternalId,
        string courseId,
        string forumInstanceId,
        string forumName,
        bool includePosts,
        int postsPerDiscussion,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ForumInfo> forums;
        try
        {
            forums = await forumGateway.GetForumsByCoursesAsync(
                userExternalId,
                courseId,
                cancellationToken);
        }
        catch
        {
            // If mod_forum_get_forums_by_courses is not available, skip the fallback.
            return null;
        }

        var forumInfo = forums.FirstOrDefault(f =>
            string.Equals(f.ForumId, forumInstanceId, StringComparison.OrdinalIgnoreCase));
        if (forumInfo is null ||
            !string.Equals(forumInfo.Type, "single", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // For a single-type forum, the discussionId is typically exposed as the
        // forum instance ID itself, or we can try fetching posts using the forumId.
        // We use mod_forum_get_discussion_posts with the forumId as a best-effort
        // attempt to locate the single discussion.
        IReadOnlyList<ForumPostSummary> posts;
        try
        {
            posts = await forumGateway.GetDiscussionPostsAsync(
                userExternalId,
                forumInstanceId,
                "created",
                "ASC",
                cancellationToken);
        }
        catch
        {
            return null;
        }

        if (posts.Count == 0)
        {
            return null;
        }

        // Derive the actual discussionId from the first post.
        var discussionId = posts[0].DiscussionId;
        if (string.IsNullOrWhiteSpace(discussionId))
        {
            discussionId = forumInstanceId;
        }

        var limitedPosts = includePosts ? posts.Take(postsPerDiscussion).ToArray() : [];
        var firstPost = posts[0];

        var syntheticDiscussion = new ForumDiscussionSummary(
            discussionId,
            firstPost.PostId,
            forumName,
            firstPost.Subject,
            firstPost.MessageText,
            firstPost.UserId,
            firstPost.UserFullName,
            firstPost.CreatedAt,
            firstPost.ModifiedAt,
            posts.Max(p => p.ModifiedAt ?? p.CreatedAt),
            posts.Count - 1,
            UnreadCount: 0,
            Pinned: null,
            Locked: null,
            CanReply: firstPost.CanReply,
            PostsReturned: limitedPosts.Length,
            PostsTotal: posts.Count,
            Posts: limitedPosts);

        return [syntheticDiscussion];
    }

    private async Task<CourseActivitySummary?> ResolveForumAsync(
        string userExternalId,
        string courseId,
        string forumId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(forumId))
        {
            return null;
        }

        var contents = await contentsGateway.GetCourseContentsAsync(
            userExternalId,
            courseId,
            ForumModuleTypes,
            includeHidden: true,
            onlyWithFiles: false,
            cancellationToken);

        var normalizedForumId = forumId.Trim();
        var module = contents.Sections
            .SelectMany(section => section.Modules)
            .FirstOrDefault(activity =>
                string.Equals(activity.ModuleType, "forum", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(activity.ModuleId, normalizedForumId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(activity.InstanceId, normalizedForumId, StringComparison.OrdinalIgnoreCase)));

        return module is null
            ? null
            : MoodleConnector.Application.Activities.ListCourseActivitiesQueryHandler.ToActivity(module);
    }

    internal static int NormalizePage(int page)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "A página deve ser maior ou igual a 1. A paginação começa em 1.");
        return page;
    }

    internal static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 25);

    internal static int NormalizePostsPerDiscussion(int postsPerDiscussion) => Math.Clamp(postsPerDiscussion, 1, 100);

    internal static string NormalizeDiscussionSortBy(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? "timemodified"
            : sortBy.Trim().ToLowerInvariant();
    }

    internal static string NormalizeSortDirection(string? sortDirection)
    {
        return string.Equals(sortDirection?.Trim(), "ASC", StringComparison.OrdinalIgnoreCase)
            ? "ASC"
            : "DESC";
    }
}
