using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MoodleConnector.Application.Pedagogy;

namespace MoodleConnector.Infrastructure.Pedagogy;

public sealed partial class MarkdownPedagogicGuidanceSearch : IPedagogicGuidanceSearch
{
    private const int MaximumBlockLength = 1600;
    private const int BlockOverlap = 100;
    private const int MaximumExcerptLength = 400;
    private const int MaximumQueryLength = 300;
    private readonly IndexedSection[] _index;

    public MarkdownPedagogicGuidanceSearch(string rootPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        cancellationToken.ThrowIfCancellationRequested();
        _index = BuildIndex(Path.GetFullPath(rootPath), cancellationToken);
    }

    public Task<IReadOnlyList<PedagogicGuidanceSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var boundedQuery = (query ?? string.Empty).Trim();
        if (boundedQuery.Length > MaximumQueryLength)
        {
            boundedQuery = boundedQuery[..MaximumQueryLength];
        }

        var terms = Normalize(boundedQuery).Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (terms.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<PedagogicGuidanceSearchResult>>([]);
        }

        var results = new List<PedagogicGuidanceSearchResult>();
        foreach (var section in _index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var combined = $"{section.NormalizedTitle} {section.NormalizedSection} {section.NormalizedBody.Text}";
            var matchingTermCount = terms.Count(term => combined.Contains(term, StringComparison.Ordinal));
            if (matchingTermCount == 0)
            {
                continue;
            }

            var score = matchingTermCount == terms.Length ? 3 : 0;
            foreach (var term in terms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (section.NormalizedTitle.Contains(term, StringComparison.Ordinal)
                    || section.NormalizedSection.Contains(term, StringComparison.Ordinal))
                {
                    score += 4;
                }

                score += CountOccurrences(section.NormalizedBody.Text, term);
            }

            results.Add(new PedagogicGuidanceSearchResult(
                section.Title,
                section.Section,
                section.RelativePath,
                CreateExcerpt(section, terms),
                score));
        }

        IReadOnlyList<PedagogicGuidanceSearchResult> ordered = results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.RelativePath, StringComparer.Ordinal)
            .ThenBy(result => result.Section, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 10))
            .ToArray();
        return Task.FromResult(ordered);
    }

    private static IndexedSection[] BuildIndex(string rootPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        var sections = new List<IndexedSection>();
        foreach (var path in Directory.EnumerateFiles(rootPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
            foreach (var block in ParseBlocks(File.ReadAllText(path)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sections.Add(new IndexedSection(
                    block.Title,
                    block.Section,
                    relativePath,
                    block.Body,
                    Normalize(block.Title).Text,
                    Normalize(block.Section).Text,
                    Normalize(block.Body),
                    CreateChunks(block.Body)));
            }
        }

        return sections.ToArray();
    }

    private static IEnumerable<Block> ParseBlocks(string markdown)
    {
        var title = string.Empty;
        var section = string.Empty;
        var body = new StringBuilder();

        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var heading = HeadingRegex().Match(line);
            if (!heading.Success)
            {
                body.AppendLine(line);
                continue;
            }

            if (body.Length > 0)
            {
                yield return new Block(title, section.Length > 0 ? section : title, body.ToString().Trim());
                body.Clear();
            }

            var headingText = heading.Groups[2].Value.Trim();
            if (title.Length == 0)
            {
                title = headingText;
            }

            section = headingText;
        }

        if (body.Length > 0 || section.Length > 0)
        {
            yield return new Block(title, section.Length > 0 ? section : title, body.ToString().Trim());
        }
    }

    private static IndexedChunk[] CreateChunks(string body)
    {
        if (body.Length <= MaximumBlockLength)
        {
            return [new IndexedChunk(0, body.Length)];
        }

        var chunks = new List<IndexedChunk>();
        var start = 0;
        while (start < body.Length)
        {
            var length = Math.Min(MaximumBlockLength, body.Length - start);
            chunks.Add(new IndexedChunk(start, length));
            if (start + length == body.Length)
            {
                break;
            }

            start += MaximumBlockLength - BlockOverlap;
        }

        return chunks.ToArray();
    }

    private static string CreateExcerpt(IndexedSection section, IReadOnlyList<string> terms)
    {
        if (section.Body.Length <= MaximumExcerptLength)
        {
            return section.Body;
        }

        var normalizedMatch = terms
            .Select(term => section.NormalizedBody.Text.IndexOf(term, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();
        var originalMatch = section.NormalizedBody.OriginalOffsets.Length == 0
            ? 0
            : section.NormalizedBody.OriginalOffsets[Math.Min(normalizedMatch, section.NormalizedBody.OriginalOffsets.Length - 1)];
        var chunk = section.Chunks.FirstOrDefault(candidate =>
            originalMatch >= candidate.Start && originalMatch < candidate.Start + candidate.Length) ?? section.Chunks[0];
        var excerptStart = Math.Clamp(
            originalMatch - 100,
            chunk.Start,
            Math.Max(chunk.Start, chunk.Start + chunk.Length - MaximumExcerptLength));
        var excerptLength = Math.Min(MaximumExcerptLength, chunk.Start + chunk.Length - excerptStart);
        return section.Body.Substring(excerptStart, excerptLength).Trim();
    }

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(term, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += term.Length;
        }

        return count;
    }

    private static NormalizedText Normalize(string value)
    {
        var text = new StringBuilder(value.Length);
        var offsets = new List<int>(value.Length);
        var previousWasWhitespace = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    text.Append(' ');
                    offsets.Add(index);
                    previousWasWhitespace = true;
                }

                continue;
            }

            previousWasWhitespace = false;
            foreach (var decomposed in character.ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(decomposed) != UnicodeCategory.NonSpacingMark)
                {
                    text.Append(char.ToLowerInvariant(decomposed));
                    offsets.Add(index);
                }
            }
        }

        return new NormalizedText(text.ToString().Normalize(NormalizationForm.FormC), offsets.ToArray());
    }

    private sealed record Block(string Title, string Section, string Body);
    private sealed record IndexedChunk(int Start, int Length);
    private sealed record NormalizedText(string Text, int[] OriginalOffsets);
    private sealed record IndexedSection(
        string Title,
        string Section,
        string RelativePath,
        string Body,
        string NormalizedTitle,
        string NormalizedSection,
        NormalizedText NormalizedBody,
        IndexedChunk[] Chunks);

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingRegex();
}
