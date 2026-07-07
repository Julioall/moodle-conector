using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Memory;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Memory;

public sealed class UserMemoryServiceTests
{
    [Fact]
    public async Task Save_normalizes_key_and_upserts_equivalent_memory()
    {
        var fixture = new Fixture();

        var first = await fixture.Service.SaveAsync(new SaveUserMemoryRequest("preferencia", "  Avaliação / Formativa  ", "Usar rubrica", "explicit"));
        var second = await fixture.Service.SaveAsync(new SaveUserMemoryRequest("preferencia", "avaliacao-formativa", "Usar rubrica detalhada", "inferred"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("avaliacao-formativa", second.NormalizedKey);
        Assert.Equal("Usar rubrica detalhada", second.Content);
        Assert.Equal("inferred", second.Origin);
        Assert.Single(fixture.Repository.Items);
        Assert.True(second.UpdatedAtUtc > second.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("senai", null)]
    [InlineData("senai", "42")]
    public async Task Save_and_list_support_global_moodle_and_course_scopes(string? alias, string? courseId)
    {
        var fixture = new Fixture();
        await fixture.Service.SaveAsync(new SaveUserMemoryRequest("caminho", "Minha chave", "conteúdo", "explicit", alias, courseId));

        var result = await fixture.Service.ListAsync(new ListUserMemoriesRequest(alias, courseId));

        var memory = Assert.Single(result);
        Assert.Equal(alias, memory.MoodleAlias);
        Assert.Equal(courseId, memory.CourseId);
    }

    [Fact]
    public async Task List_is_isolated_by_current_subject_and_clamps_limit()
    {
        var repository = new FakeRepository();
        var alice = new UserMemoryService(repository, new FakeCurrentUser("alice"), new SteppingTimeProvider());
        var bob = new UserMemoryService(repository, new FakeCurrentUser("bob"), new SteppingTimeProvider());
        await alice.SaveAsync(new("decisao", "chave-a", "A", "explicit"));
        await bob.SaveAsync(new("decisao", "chave-b", "B", "explicit"));

        var aliceItems = await alice.ListAsync(new(Limit: 500));

        Assert.Single(aliceItems);
        Assert.Equal("alice", aliceItems[0].OwnerSubject);
        Assert.Equal(50, repository.LastListLimit);
    }

    [Fact]
    public async Task List_uses_default_limit_twenty()
    {
        var fixture = new Fixture();
        await fixture.Service.ListAsync(new());
        Assert.Equal(20, fixture.Repository.LastListLimit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Operations_reject_empty_identity(string subject)
    {
        var service = new UserMemoryService(new FakeRepository(), new FakeCurrentUser(subject), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new("preferencia", "chave", "valor", "explicit")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("invalid", "key", "content", "explicit", null, null)]
    [InlineData("preferencia", "key", "content", "unknown", null, null)]
    [InlineData("preferencia", "", "content", "explicit", null, null)]
    [InlineData("preferencia", "key", "", "explicit", null, null)]
    [InlineData("preferencia", "key", "content", "explicit", null, "42")]
    public async Task Save_rejects_invalid_values(string category, string key, string content, string origin, string? alias, string? courseId)
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(new(category, key, content, origin, alias, courseId)));
    }

    [Theory]
    [InlineData("password=abc")]
    [InlineData("minha senha secreta")]
    [InlineData("token: abc")]
    [InlineData("api key xyz")]
    [InlineData("secret=abc")]
    [InlineData("cookie session")]
    [InlineData("Bearer abc")]
    [InlineData("sk-proj-abc")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature")]
    public async Task Save_rejects_secret_patterns(string content)
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(new("correcao", "chave", content, "explicit")));
    }

    [Fact]
    public async Task Save_rejects_structural_jwt_whose_header_does_not_start_with_eyJ()
    {
        var fixture = new Fixture();
        const string jwtWithLeadingWhitespaceInHeader = "IHsiYWxnIjoiSFMyNTYifQ.eyJzdWIiOiIxIn0.signature";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.SaveAsync(new("correcao", "chave", jwtWithLeadingWhitespaceInHeader, "explicit")));
    }

    [Fact]
    public async Task Save_rejects_unsigned_alg_none_jwt_with_empty_signature()
    {
        var fixture = new Fixture();
        const string unsignedJwt = "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.SaveAsync(new("correcao", "chave", unsignedJwt, "explicit")));
    }

    [Fact]
    public async Task Save_does_not_reject_arbitrary_three_segment_text_as_jwt()
    {
        var fixture = new Fixture();

        var saved = await fixture.Service.SaveAsync(new("correcao", "chave", "abc.def.ghi", "explicit"));

        Assert.Equal("abc.def.ghi", saved.Content);
    }

    [Fact]
    public async Task Save_enforces_length_limits()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(new("preferencia", new string('a', 121), "x", "explicit")));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(new("preferencia", "key", new string('a', 1001), "explicit")));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(new("preferencia", "key", "x", "explicit", new string('a', 65))));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(new("preferencia", "key", "x", "explicit", "a", new string('1', 65))));
    }

    [Fact]
    public async Task Remove_is_owner_only_and_indistinguishable_for_missing_or_foreign_ids()
    {
        var repository = new FakeRepository();
        var alice = new UserMemoryService(repository, new FakeCurrentUser("alice"), TimeProvider.System);
        var bob = new UserMemoryService(repository, new FakeCurrentUser("bob"), TimeProvider.System);
        var saved = await alice.SaveAsync(new("decisao", "key", "content", "explicit"));

        var foreign = await bob.RemoveAsync(saved.Id);
        var missing = await bob.RemoveAsync(Guid.NewGuid());

        Assert.Equal(missing, foreign);
        Assert.False(foreign.Removed);
        Assert.Single(repository.Items);
        Assert.True((await alice.RemoveAsync(saved.Id)).Removed);
        Assert.Empty(repository.Items);
    }

    private sealed class Fixture
    {
        public FakeRepository Repository { get; } = new();
        public UserMemoryService Service { get; }

        public Fixture() => Service = new(Repository, new FakeCurrentUser("user-1"), new SteppingTimeProvider());
    }

    private sealed class FakeCurrentUser(string subject) : ICurrentUserContext
    {
        public string Subject => subject;
        public string? Email => null;
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasScope(string scope) => false;
    }

    private sealed class SteppingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now = _now.AddSeconds(1);
    }

    private sealed class FakeRepository : IUserMemoryRepository
    {
        public List<UserMemory> Items { get; } = [];
        public int LastListLimit { get; private set; }

        public Task<UserMemory?> FindEquivalentAsync(string ownerSubject, string category, string? moodleAlias, string? courseId, string normalizedKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(x => x.OwnerSubject == ownerSubject && x.Category == category && x.MoodleAlias == moodleAlias && x.CourseId == courseId && x.NormalizedKey == normalizedKey));

        public Task AddAsync(UserMemory memory, CancellationToken cancellationToken = default)
        {
            Items.Add(memory);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserMemory>> ListAsync(string ownerSubject, string? moodleAlias, string? courseId, int limit, CancellationToken cancellationToken = default)
        {
            LastListLimit = limit;
            return Task.FromResult<IReadOnlyList<UserMemory>>(Items.Where(x => x.OwnerSubject == ownerSubject && x.MoodleAlias == moodleAlias && x.CourseId == courseId).Take(limit).ToList());
        }

        public Task<bool> RemoveAsync(Guid id, string ownerSubject, CancellationToken cancellationToken = default)
        {
            var item = Items.SingleOrDefault(x => x.Id == id && x.OwnerSubject == ownerSubject);
            return Task.FromResult(item is not null && Items.Remove(item));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
