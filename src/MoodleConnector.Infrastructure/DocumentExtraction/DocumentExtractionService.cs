using System.Text;
using System.Text.RegularExpressions;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure.DocumentExtraction;

public sealed partial class DocumentExtractionService : IDocumentExtractionService
{
    private const int MaxExtractedChars = 120_000;

    private static readonly HashSet<string> SupportedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/html",
        "text/htm",
        "application/json",
        "application/xml",
        "text/xml",
        "text/csv"
    };

    private static readonly HashSet<string> BinaryMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.oasis.opendocument.text",
        "application/zip",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp"
    };

    public Task<DocumentExtractionResult> ExtractAsync(
        string filename,
        string mimeType,
        byte[] content,
        CancellationToken cancellationToken)
    {
        if (content is null || content.Length == 0)
        {
            return Task.FromResult(new DocumentExtractionResult(
                filename,
                mimeType,
                ExtractionStatus.Empty,
                ExtractedText: null,
                WordCount: 0,
                CharCount: 0,
                Truncated: false,
                ErrorMessage: "O arquivo esta vazio."));
        }

        if (BinaryMimeTypes.Contains(mimeType))
        {
            return Task.FromResult(new DocumentExtractionResult(
                filename,
                mimeType,
                ExtractionStatus.UnsupportedFormat,
                ExtractedText: null,
                WordCount: 0,
                CharCount: 0,
                Truncated: false,
                ErrorMessage: $"Extracao de texto para o formato '{mimeType}' nao esta disponivel nesta versao. Requer biblioteca de conversao externa."));
        }

        if (!SupportedMimeTypes.Contains(mimeType))
        {
            return Task.FromResult(new DocumentExtractionResult(
                filename,
                mimeType,
                ExtractionStatus.UnsupportedFormat,
                ExtractedText: null,
                WordCount: 0,
                CharCount: 0,
                Truncated: false,
                ErrorMessage: $"Formato nao suportado para extracao de texto: '{mimeType}'."));
        }

        try
        {
            var raw = DecodeText(content);
            var text = NormalizeText(mimeType, raw);
            var truncated = text.Length > MaxExtractedChars;
            var extracted = truncated ? text[..MaxExtractedChars] : text;

            return Task.FromResult(new DocumentExtractionResult(
                filename,
                mimeType,
                ExtractionStatus.Succeeded,
                extracted,
                CountWords(extracted),
                extracted.Length,
                truncated,
                ErrorMessage: null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DocumentExtractionResult(
                filename,
                mimeType,
                ExtractionStatus.Failed,
                ExtractedText: null,
                WordCount: 0,
                CharCount: 0,
                Truncated: false,
                ErrorMessage: $"Falha ao extrair texto: {ex.Message}"));
        }
    }

    private static string DecodeText(byte[] content)
    {
        // Detecta BOM UTF-8, UTF-16 e tenta UTF-8 como padrão
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }

        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
        }

        try
        {
            return Encoding.UTF8.GetString(content);
        }
        catch
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private static string NormalizeText(string mimeType, string raw)
    {
        if (mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("text/htm", StringComparison.OrdinalIgnoreCase))
        {
            return StripHtmlTags(raw);
        }

        return raw.Trim();
    }

    private static string StripHtmlTags(string html)
    {
        var noScript = ScriptTagRegex().Replace(html, " ");
        var noStyle = StyleTagRegex().Replace(noScript, " ");
        var noTags = HtmlTagRegex().Replace(noStyle, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        var collapsed = MultiSpaceRegex().Replace(decoded, " ");
        return collapsed.Trim();
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return WordBoundaryRegex().Matches(text).Count;
    }

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\b\w+\b")]
    private static partial Regex WordBoundaryRegex();
}
