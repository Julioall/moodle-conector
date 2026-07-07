using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MoodleConnector.Application.Pedagogy;

namespace MoodleConnector.Infrastructure.Pedagogy;

public sealed partial class MarkdownPedagogicGuidanceSearch(string rootPath) : IPedagogicGuidanceSearch
{
    private const int MaximumBlockLength = 1600;
    private const int MaximumQueryLength = 300;
    private readonly string _rootPath = Path.GetFullPath(rootPath ?? throw new ArgumentNullException(nameof(rootPath)));

    public async Task<IReadOnlyList<PedagogicGuidanceSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootPath))
        {
            return [];
        }

        var boundedQuery = (query ?? string.Empty).Trim();
        if (boundedQuery.Length > MaximumQueryLength)
        {
            boundedQuery = boundedQuery[..MaximumQueryLength];
        }

        var terms = Normalize(boundedQuery)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (terms.Length == 0)
        {
            return [];
        }

        var results = new List<PedagogicGuidanceSearchResult>();
        foreach (var path in Directory.EnumerateFiles(_rootPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var markdown = await File.ReadAllTextAsync(path, cancellationToken);
            foreach (var block in ParseBlocks(markdown))
            {
                foreach (var bodyChunk in Chunk(block.Body))
                {
                    var normalizedTitle = Normalize(block.Title);
                    var normalizedSection = Normalize(block.Section);
                    var normalizedBody = Normalize(bodyChunk);
                    var combined = $"{normalizedTitle} {normalizedSection} {normalizedBody}";
                    if (!terms.All(term => combined.Contains(term, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var score = 3;
                    foreach (var term in terms)
                    {
                        if (normalizedTitle.Contains(term, StringComparison.Ordinal)
                            || normalizedSection.Contains(term, StringComparison.Ordinal))
                        {
                            score += 4;
                        }

                        score += CountOccurrences(normalizedBody, term);
                    }

                    results.Add(new PedagogicGuidanceSearchResult(
                        block.Title,
                        block.Section,
                        Path.GetRelativePath(_rootPath, path).Replace('\\', '/'),
                        CreateExcerpt(bodyChunk, terms),
                        score));
                }
            }
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.RelativePath, StringComparer.Ordinal)
            .ThenBy(result => result.Section, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 10))
            .ToArray();
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

    private static IEnumerable<string> Chunk(string body)
    {
        if (body.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        for (var start = 0; start < body.Length; start += MaximumBlockLength)
        {
            yield return body.Substring(start, Math.Min(MaximumBlockLength, body.Length - start));
        }
    }

    private static string CreateExcerpt(string body, IReadOnlyList<string> terms)
    {
        const int maximumExcerptLength = 400;
        if (body.Length <= maximumExcerptLength)
        {
            return body;
        }

        var normalized = Normalize(body);
        var matchIndex = terms.Select(term => normalized.IndexOf(term, StringComparison.Ordinal)).Where(index => index >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Clamp(matchIndex - 100, 0, body.Length - maximumExcerptLength);
        return body.Substring(start, maximumExcerptLength).Trim();
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

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return WhitespaceRegex().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ");
    }

    private sealed record Block(string Title, string Section, string Body);

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
