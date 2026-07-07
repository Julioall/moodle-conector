using MoodleConnector.Infrastructure.Pedagogy;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MarkdownPedagogicGuidanceSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pedagogy-{Guid.NewGuid():N}");

    public MarkdownPedagogicGuidanceSearchTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task SearchAsync_RanksTitleAndSectionMatchesAboveBodyMatches()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "body.md"), "# Outro guia\n## Prática\nA avaliação formativa ajuda a aprendizagem.");
        await File.WriteAllTextAsync(Path.Combine(_root, "title.md"), "# Avaliação Formativa\n## Estratégias\nUse devolutivas frequentes.");

        var results = await Search().SearchAsync("avaliacao formativa", 10, CancellationToken.None);

        Assert.Equal("title.md", results[0].RelativePath);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task SearchAsync_PreservesDocumentAndSectionMetadataAndReturnsRelevantExcerpt()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "guia.md"), "# Guia do Tutor\nIntrodução geral.\n## Avaliação contínua\nObserve evidências formativas e ofereça feedback.");

        var result = Assert.Single(await Search().SearchAsync("evidencias formativas", 10, CancellationToken.None));

        Assert.Equal("Guia do Tutor", result.Title);
        Assert.Equal("Avaliação contínua", result.Section);
        Assert.Equal("guia.md", result.RelativePath);
        Assert.Contains("evidências formativas", result.Excerpt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_IndexesOnlyMarkdownFilesAtRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "subdir"));
        await File.WriteAllTextAsync(Path.Combine(_root, "valid.md"), "# Válido\n## Tema\ntermo procurado");
        await File.WriteAllTextAsync(Path.Combine(_root, "ignored.txt"), "# Ignorado\ntermo procurado");
        await File.WriteAllTextAsync(Path.Combine(_root, "subdir", "nested.md"), "# Aninhado\ntermo procurado");

        var result = Assert.Single(await Search().SearchAsync("termo procurado", 10, CancellationToken.None));

        Assert.Equal("valid.md", result.RelativePath);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyWhenRootDoesNotExist()
    {
        var missing = new MarkdownPedagogicGuidanceSearch(Path.Combine(_root, "missing"));

        Assert.Empty(await missing.SearchAsync("qualquer", 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_IsDeterministicAndClampsQueryAndLimit()
    {
        for (var index = 0; index < 12; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(_root, $"{index:D2}.md"), $"# Mesmo\n## Seção\ntermo {index}");
        }

        var search = Search();
        var first = await search.SearchAsync(new string('x', 300) + " termo", 99, CancellationToken.None);
        var second = await search.SearchAsync("termo", 0, CancellationToken.None);

        Assert.Empty(first);
        Assert.Single(second);
        Assert.Equal("00.md", second[0].RelativePath);
    }

    [Fact]
    public async Task SearchAsync_SplitsLongSectionsWithoutLosingSearchableText()
    {
        var filler = new string('a', 1700);
        await File.WriteAllTextAsync(Path.Combine(_root, "long.md"), $"# Guia\n## Longa\n{filler} palavra-final");

        var result = Assert.Single(await Search().SearchAsync("palavra-final", 10, CancellationToken.None));

        Assert.Equal("Longa", result.Section);
        Assert.Contains("palavra-final", result.Excerpt);
    }

    private MarkdownPedagogicGuidanceSearch Search() => new(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
