using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleForumGateway
{
    Task<IReadOnlyList<ForumDiscussionSummary>> GetForumDiscussionsPaginatedAsync(
        string userExternalId,
        string forumId,
        string sortBy,
        string sortDirection,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ForumPostSummary>> GetDiscussionPostsAsync(
        string userExternalId,
        string discussionId,
        string sortBy,
        string sortDirection,
        CancellationToken cancellationToken);
}
