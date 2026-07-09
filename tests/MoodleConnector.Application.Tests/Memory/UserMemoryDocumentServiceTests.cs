using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Memory;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Memory;

public sealed class UserMemoryDocumentServiceTests
{
    [Fact]
    public async Task SaveAsync_persists_large_document_and_creates_short_model_memory_link()
    {
        var memoryRepository = new FakeMemoryRepository();
        var documentRepository = new FakeDocumentRepository();
        var currentUser = new FakeCurrentUser("user-1");
        var clock = new SteppingTimeProvider();
        var memoryService = new UserMemoryService(memoryRepository, currentUser, clock);
        var sut = new UserMemoryDocumentService(documentRepository, memoryService, currentUser, clock);
        var largeHtml = "<!-- CRONOGRAMA DE ATIVIDADES -->" + new string('x', 5000);

        var saved = await sut.SaveAsync(new SaveUserMemoryDocumentRequest(
            "cronograma atividades",
            "Cronograma de Atividades",
            largeHtml,
            "html",
            "explicit",
            "senai",
            "42"));

        Assert.Equal("cronograma-atividades", saved.NormalizedKey);
        Assert.Equal("html", saved.Format);
        Assert.Equal(largeHtml, saved.Content);
        var memory = Assert.Single(memoryRepository.Items);
        Assert.Equal("modelo", memory.Category);
        Assert.Equal("cronograma-atividades", memory.NormalizedKey);
        Assert.Equal(saved.Id, memory.LinkedDocumentId);
        Assert.True(memory.Content.Length <= 1000);
        Assert.Contains(saved.Id.ToString(), memory.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_returns_only_documents_owned_by_current_user()
    {
        var documentRepository = new FakeDocumentRepository();
        var alice = CreateService(documentRepository, "alice");
        var bob = CreateService(documentRepository, "bob");
        var saved = await alice.SaveAsync(new("modelo", "Modelo", "conteudo", "markdown", "explicit"));

        var aliceRead = await alice.ReadAsync(saved.Id);
        var bobRead = await bob.ReadAsync(saved.Id);

        Assert.NotNull(aliceRead);
        Assert.Null(bobRead);
    }

    [Fact]
    public async Task RemoveAsync_deletes_owned_document_and_matching_memory_link()
    {
        var memoryRepository = new FakeMemoryRepository();
        var documentRepository = new FakeDocumentRepository();
        var currentUser = new FakeCurrentUser("user-1");
        var clock = new SteppingTimeProvider();
        var memoryService = new UserMemoryService(memoryRepository, currentUser, clock);
        var sut = new UserMemoryDocumentService(documentRepository, memoryService, currentUser, clock);
        var saved = await sut.SaveAsync(new("cronograma", "Cronograma", "conteudo", "markdown", "explicit"));

        var removed = await sut.RemoveAsync(saved.Id);

        Assert.True(removed.Removed);
        Assert.Empty(documentRepository.Items);
        Assert.Empty(memoryRepository.Items);
    }

    private static UserMemoryDocumentService CreateService(FakeDocumentRepository repository, string subject)
    {
        var currentUser = new FakeCurrentUser(subject);
        return new UserMemoryDocumentService(repository, new UserMemoryService(new FakeMemoryRepository(), currentUser, new SteppingTimeProvider()), currentUser, new SteppingTimeProvider());
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

    private sealed class FakeDocumentRepository : IUserMemoryDocumentRepository
    {
        public List<UserMemoryDocument> Items { get; } = [];

        public Task<UserMemoryDocument> UpsertAsync(UserMemoryDocument candidate, CancellationToken cancellationToken = default)
        {
            var existing = Items.SingleOrDefault(x => x.OwnerSubject == candidate.OwnerSubject && x.MoodleAlias == candidate.MoodleAlias && x.CourseId == candidate.CourseId && x.NormalizedKey == candidate.NormalizedKey);
            if (existing is null)
            {
                Items.Add(candidate);
                return Task.FromResult(candidate);
            }

            existing.Update(candidate.Title, candidate.Content, candidate.Format, candidate.Origin, candidate.UpdatedAtUtc);
            return Task.FromResult(existing);
        }

        public Task<UserMemoryDocument?> FindOwnedAsync(Guid id, string ownerSubject, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(x => x.Id == id && x.OwnerSubject == ownerSubject));

        public Task<IReadOnlyList<UserMemoryDocument>> ListAsync(string ownerSubject, string? moodleAlias, string? courseId, string? query, string? normalizedKeyQuery, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserMemoryDocument>>(Items
                .Where(x => x.OwnerSubject == ownerSubject && (moodleAlias is null || x.MoodleAlias == moodleAlias) && (courseId is null || x.CourseId == courseId))
                .Where(x => query is null || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Content.Contains(query, StringComparison.OrdinalIgnoreCase) || (normalizedKeyQuery is not null && x.NormalizedKey.Contains(normalizedKeyQuery, StringComparison.OrdinalIgnoreCase)))
                .Take(limit)
                .ToList());

        public Task<bool> RemoveAsync(Guid id, string ownerSubject, CancellationToken cancellationToken = default)
        {
            var item = Items.SingleOrDefault(x => x.Id == id && x.OwnerSubject == ownerSubject);
            return Task.FromResult(item is not null && Items.Remove(item));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMemoryRepository : IUserMemoryRepository
    {
        public List<UserMemory> Items { get; } = [];

        public Task<UserMemory?> FindEquivalentAsync(string ownerSubject, string category, string? moodleAlias, string? courseId, string normalizedKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(x => x.OwnerSubject == ownerSubject && x.Category == category && x.MoodleAlias == moodleAlias && x.CourseId == courseId && x.NormalizedKey == normalizedKey));

        public Task AddAsync(UserMemory memory, CancellationToken cancellationToken = default)
        {
            Items.Add(memory);
            return Task.CompletedTask;
        }

        public Task<UserMemory> UpsertAsync(UserMemory candidate, CancellationToken cancellationToken = default)
        {
            var existing = Items.SingleOrDefault(x => x.OwnerSubject == candidate.OwnerSubject && x.Category == candidate.Category && x.MoodleAlias == candidate.MoodleAlias && x.CourseId == candidate.CourseId && x.NormalizedKey == candidate.NormalizedKey);
            if (existing is null)
            {
                Items.Add(candidate);
                return Task.FromResult(candidate);
            }

            existing.Update(candidate.Content, candidate.Origin, candidate.UpdatedAtUtc, candidate.LinkedDocumentId);
            return Task.FromResult(existing);
        }

        public Task<IReadOnlyList<UserMemory>> ListAsync(string ownerSubject, string? moodleAlias, string? courseId, string? category, string? contentQuery, string? normalizedKeyQuery, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserMemory>>(Items
                .Where(x => x.OwnerSubject == ownerSubject)
                .Where(x => category is null || x.Category == category)
                .Where(x => normalizedKeyQuery is null || x.NormalizedKey.Contains(normalizedKeyQuery, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList());

        public Task<UserMemory?> FindOwnedAsync(Guid id, string ownerSubject, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(x => x.Id == id && x.OwnerSubject == ownerSubject));

        public Task<bool> RemoveAsync(Guid id, string ownerSubject, CancellationToken cancellationToken = default)
        {
            var item = Items.SingleOrDefault(x => x.Id == id && x.OwnerSubject == ownerSubject);
            return Task.FromResult(item is not null && Items.Remove(item));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
