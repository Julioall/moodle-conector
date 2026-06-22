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

    internal static int NormalizePage(int page) => Math.Max(1, page);

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
