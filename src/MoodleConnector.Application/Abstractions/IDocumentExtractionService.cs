namespace MoodleConnector.Application.Abstractions;

public interface IDocumentExtractionService
{
    /// <summary>
    /// Extrai texto e metadados de um arquivo de submissao.
    /// Suporta TXT, HTML, JSON nativamente; retorna stub estruturado para formatos binarios.
    /// </summary>
    Task<DocumentExtractionResult> ExtractAsync(
        string filename,
        string mimeType,
        byte[] content,
        CancellationToken cancellationToken);
}

public sealed record DocumentExtractionResult(
    string Filename,
    string MimeType,
    string ExtractionStatus,
    string? ExtractedText,
    int WordCount,
    int CharCount,
    bool Truncated,
    string? ErrorMessage);

public static class ExtractionStatus
{
    public const string Succeeded = "succeeded";
    public const string UnsupportedFormat = "unsupported_format";
    public const string FileTooLarge = "file_too_large";
    public const string Empty = "empty";
    public const string Failed = "failed";
}
