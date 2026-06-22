using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Forums;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Forums;

public sealed class ReadForumQueryHandlerTests
{
    [Fact]
    public async Task Deve_resolver_forum_e_ler_discussoes_com_posts()
    {
        var forumGateway = new FakeForumGateway
        {
            Discussions = [Discussion("9001", "8001", "Avisos")],
            Posts =
            [
                Post("8001", "9001", "Avisos", "Primeira mensagem"),
                Post("8002", "9001", "Re: Avisos", "Resposta")
            ]
        };
        var sut = CreateHandler(forumGateway: forumGateway);

        var result = await sut.Handle(
            new ReadForumQuery(
                "usuario-42",
                "CURSO-1",
                "21",
                Page: 1,
                PageSize: 10,
                SortBy: "timemodified",
                SortDirection: "DESC",
                IncludePosts: true,
                PostsPerDiscussion: 1),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("701", forumGateway.LastForumId);
        Assert.Equal(11, forumGateway.LastPageSize);
        Assert.Equal(["9001"], forumGateway.PostDiscussionIds);
        Assert.Equal("701", result!.ForumId);
        Assert.Equal("21", result.ForumModuleId);
        Assert.Equal("Forum Geral", result.ForumName);
        Assert.Single(result.Discussions);
        Assert.Equal(1, result.Discussions[0].PostsReturned);
        Assert.Equal(2, result.Discussions[0].PostsTotal);
        Assert.Equal("Primeira mensagem", result.Discussions[0].Posts[0].MessageText);
    }

    [Fact]
    public async Task Deve_paginar_discussoes_sem_buscar_posts_quando_desabilitado()
    {
        var forumGateway = new FakeForumGateway
        {
            Discussions =
            [
                Discussion("9001", "8001", "Topico 1"),
                Discussion("9002", "8002", "Topico 2"),
                Discussion("9003", "8003", "Topico 3")
            ]
        };
        var sut = CreateHandler(forumGateway: forumGateway);

        var result = await sut.Handle(
            new ReadForumQuery(
                "usuario-42",
                "CURSO-1",
                "701",
                Page: 1,
                PageSize: 2,
                SortBy: "id",
                SortDirection: "ASC",
                IncludePosts: false,
                PostsPerDiscussion: 50),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, forumGateway.LastPageSize);
        Assert.True(result!.HasMore);
        Assert.Equal(2, result.ReturnedCount);
        Assert.Empty(forumGateway.PostDiscussionIds);
    }

    [Fact]
    public async Task Deve_retornar_null_para_forum_inexistente()
    {
        var forumGateway = new FakeForumGateway();
        var sut = CreateHandler(
            contentsGateway: new FakeContentsGateway { ReturnWithoutForum = true },
            forumGateway: forumGateway);

        var result = await sut.Handle(
            new ReadForumQuery(
                "usuario-42",
                "CURSO-1",
                "999",
                Page: 1,
                PageSize: 10,
                SortBy: "timemodified",
                SortDirection: "DESC",
                IncludePosts: true,
                PostsPerDiscussion: 50),
            CancellationToken.None);

        Assert.Null(result);
        Assert.False(forumGateway.WasDiscussionsCalled);
    }

    private static ReadForumQueryHandler CreateHandler(
        FakeContentsGateway? contentsGateway = null,
        FakeForumGateway? forumGateway = null)
    {
        return new ReadForumQueryHandler(
            new FakeCoursesGateway(),
            contentsGateway ?? new FakeContentsGateway(),
            forumGateway ?? new FakeForumGateway());
    }

    private static ForumDiscussionSummary Discussion(string discussionId, string firstPostId, string subject)
    {
        return new ForumDiscussionSummary(
            discussionId,
            firstPostId,
            subject,
            subject,
            "Mensagem inicial",
            "101",
            "Ana Souza",
            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero),
            ReplyCount: 1,
            UnreadCount: 0,
            Pinned: false,
            Locked: false,
            CanReply: true,
            PostsReturned: 0,
            PostsTotal: 0,
            Posts: []);
    }

    private static ForumPostSummary Post(string postId, string discussionId, string subject, string text)
    {
        return new ForumPostSummary(
            postId,
            discussionId,
            ParentPostId: null,
            UserId: "101",
            UserFullName: "Ana Souza",
            subject,
            text,
            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 10, 5, 0, TimeSpan.Zero),
            Deleted: false,
            CanReply: true,
            PostRead: true,
            IsPrivateReply: false,
            Children: [],
            Attachments: []);
    }

    private sealed class FakeCoursesGateway : IMoodleCoursesGateway
    {
        public Task<IReadOnlyList<CourseSummary>> GetMyCoursesAsync(
            string userExternalId,
            int limit,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<CourseSummary>> SearchMyCoursesAsync(
            string userExternalId,
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CourseSummary?> GetMyCourseAsync(
            string userExternalId,
            string courseId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CourseSummary?>(new CourseSummary(
                "123",
                "ID-123",
                "CURSO-1",
                "Curso 1",
                "Curso 1",
                10,
                "Categoria",
                null,
                null,
                true,
                null,
                null,
                null,
                null,
                null,
                null));
        }
    }

    private sealed class FakeContentsGateway : IMoodleCourseContentsGateway
    {
        public bool ReturnWithoutForum { get; init; }

        public Task<CourseContentsSummary> GetCourseContentsAsync(
            string userExternalId,
            string courseId,
            IReadOnlyCollection<string> moduleTypes,
            bool includeHidden,
            bool onlyWithFiles,
            CancellationToken cancellationToken)
        {
            var modules = ReturnWithoutForum
                ? Array.Empty<CourseModuleSummary>()
                :
                [
                    new CourseModuleSummary(
                        "21",
                        "701",
                        "forum",
                        "Forum Geral",
                        "https://moodle.example/mod/forum/view.php?id=21",
                        true,
                        true,
                        "Forum do curso",
                        null,
                        [],
                        [])
                ];

            return Task.FromResult(new CourseContentsSummary(
                courseId,
                moduleTypes.ToArray(),
                includeHidden,
                onlyWithFiles,
                [new CourseSectionSummary("1", 1, "Topico 1", null, true, modules.Length, modules.Length == 0, modules)]));
        }
    }

    private sealed class FakeForumGateway : IMoodleForumGateway
    {
        public IReadOnlyList<ForumDiscussionSummary> Discussions { get; init; } = [];

        public IReadOnlyList<ForumPostSummary> Posts { get; init; } = [];

        public IReadOnlyList<ForumInfo> Forums { get; init; } = [];

        public bool WasDiscussionsCalled { get; private set; }

        public string LastForumId { get; private set; } = string.Empty;

        public int LastPageSize { get; private set; }

        public List<string> PostDiscussionIds { get; } = [];

        public Task<IReadOnlyList<ForumDiscussionSummary>> GetForumDiscussionsPaginatedAsync(
            string userExternalId,
            string forumId,
            string sortBy,
            string sortDirection,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            WasDiscussionsCalled = true;
            LastForumId = forumId;
            LastPageSize = pageSize;
            return Task.FromResult(Discussions);
        }

        public Task<IReadOnlyList<ForumPostSummary>> GetDiscussionPostsAsync(
            string userExternalId,
            string discussionId,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken)
        {
            PostDiscussionIds.Add(discussionId);
            return Task.FromResult<IReadOnlyList<ForumPostSummary>>(
                Posts.Where(post => post.DiscussionId == discussionId).ToArray());
        }

        public Task<ForumWriteResult> AddDiscussionAsync(
            string userExternalId,
            string forumId,
            string subject,
            string messageHtml,
            int groupId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ForumWriteResult> AddDiscussionPostAsync(
            string userExternalId,
            string postId,
            string subject,
            string messageHtml,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ForumInfo>> GetForumsByCoursesAsync(
            string userExternalId,
            string courseId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Forums);
        }
    }
}
