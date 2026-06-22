using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Forums;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Application.Tests.Tools.Forums;

public sealed class MoodleForumToolsTests
{
    [Fact]
    public async Task Deve_ler_forum_com_discussoes_e_posts()
    {
        var mediator = new FakeMediator();
        var selection = new FakeMoodleConnectionSelection();
        var sut = new MoodleForumTools(mediator, selection, new FakeMoodleUserResolver(777));

        var result = await sut.LerForumAsync(
            "CURSO",
            "21",
            pagina: 2,
            tamanhoPagina: 5,
            moodleAlias: "goias");

        Assert.False(result.IsError ?? false);
        Assert.Equal("goias", selection.Alias);
        Assert.NotNull(mediator.LastQuery);
        Assert.Equal("777", mediator.LastQuery!.UserExternalId);
        Assert.Equal("CURSO", mediator.LastQuery.CourseId);
        Assert.Equal("21", mediator.LastQuery.ForumId);
        Assert.Equal(2, mediator.LastQuery.Page);
        Assert.Equal(5, mediator.LastQuery.PageSize);
        Assert.True(mediator.LastQuery.IncludePosts);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var data = structured.GetProperty("data");
        Assert.Equal("701", data.GetProperty("forumId").GetString());
        Assert.Equal("21", data.GetProperty("forumModuleId").GetString());
        var discussion = data.GetProperty("discussions")[0];
        Assert.Equal("9001", discussion.GetProperty("discussionId").GetString());
        Assert.Equal(1, discussion.GetProperty("postsReturned").GetInt32());
        Assert.Equal("Primeira mensagem", discussion.GetProperty("posts")[0].GetProperty("messageText").GetString());
        Assert.False(discussion.GetProperty("posts")[0].TryGetProperty("messageHtml", out _));
    }

    [Fact]
    public async Task Deve_rejeitar_ordenacao_invalida()
    {
        var sut = new MoodleForumTools(
            new FakeMediator(),
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.LerForumAsync("CURSO", "21", ordenarPor: "subject");

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("error", structured.GetProperty("status").GetString());
        Assert.Equal("Campo de ordenacao invalido. Use id, timemodified, timestart ou timeend.", structured.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task Deve_retornar_erro_controlado_quando_moodle_falhar()
    {
        var sut = new MoodleForumTools(
            new FakeMediator { ThrowOnRead = true },
            new FakeMoodleConnectionSelection(),
            new FakeMoodleUserResolver(777));

        var result = await sut.LerForumAsync("CURSO", "21");

        Assert.True(result.IsError ?? false);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("Nao foi possivel ler o forum no Moodle neste momento.", structured.GetProperty("warnings")[0].GetString());
    }

    private sealed class FakeMoodleConnectionSelection : IMoodleConnectionSelection
    {
        public string? Alias { get; set; }
    }

    private sealed class FakeMoodleUserResolver(long? userId) : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(userId);
        }
    }

    private sealed class FakeMediator : IMediator
    {
        public ReadForumQuery? LastQuery { get; private set; }

        public bool ThrowOnRead { get; init; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ReadForumQuery readForum)
            {
                LastQuery = readForum;
                if (ThrowOnRead)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                return Task.FromResult((TResponse)(object)CreatePage(readForum));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is ReadForumQuery readForum)
            {
                LastQuery = readForum;
                if (ThrowOnRead)
                {
                    throw new InvalidOperationException("Falha simulada.");
                }

                return Task.FromResult<object?>(CreatePage(readForum));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();

        private static ForumReadPage CreatePage(ReadForumQuery query)
        {
            var discussion = new ForumDiscussionSummary(
                "9001",
                "8001",
                "Avisos",
                "Avisos",
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
                PostsReturned: 1,
                PostsTotal: 1,
                Posts:
                [
                    new ForumPostSummary(
                        "8001",
                        "9001",
                        ParentPostId: null,
                        UserId: "101",
                        UserFullName: "Ana Souza",
                        "Avisos",
                        "Primeira mensagem",
                        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 6, 1, 10, 5, 0, TimeSpan.Zero),
                        Deleted: false,
                        CanReply: true,
                        PostRead: true,
                        IsPrivateReply: false,
                        Children: [],
                        Attachments: [])
                ]);

            return new ForumReadPage(
                "123",
                "701",
                "21",
                "Forum Geral",
                query.Page,
                query.PageSize,
                query.SortBy,
                query.SortDirection,
                query.IncludePosts,
                query.PostsPerDiscussion,
                ReturnedCount: 1,
                HasMore: false,
                [discussion]);
        }
    }
}
