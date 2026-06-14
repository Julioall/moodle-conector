using System.Text;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure.DocumentExtraction;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class DocumentExtractionServiceTests
{
    private readonly DocumentExtractionService _sut = new();

    [Fact]
    public async Task ExtractAsync_TextoPlano_RetornaTextoLimpo()
    {
        var content = Encoding.UTF8.GetBytes("Ola mundo! Este e um texto de submissao com varios termos.");

        var result = await _sut.ExtractAsync("resposta.txt", "text/plain", content, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.ExtractionStatus);
        Assert.NotEmpty(result.ExtractedText!);
        Assert.True(result.WordCount > 0);
        Assert.False(result.Truncated);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ExtractAsync_Html_RemoveTagsERetornaTexto()
    {
        const string html = "<html><body><h1>Titulo</h1><p>Paragrafo <strong>em negrito</strong>.</p></body></html>";
        var content = Encoding.UTF8.GetBytes(html);

        var result = await _sut.ExtractAsync("resposta.html", "text/html", content, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.ExtractionStatus);
        Assert.Contains("Titulo", result.ExtractedText);
        Assert.Contains("Paragrafo", result.ExtractedText);
        Assert.DoesNotContain("<h1>", result.ExtractedText);
        Assert.DoesNotContain("<p>", result.ExtractedText);
    }

    [Fact]
    public async Task ExtractAsync_PdfComTexto_RetornaTextoExtraido()
    {
        var content = CreateMinimalPdf("Ola PDF Moodle");

        var result = await _sut.ExtractAsync("relatorio.pdf", "application/pdf", content, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.ExtractionStatus);
        Assert.Contains("Ola PDF Moodle", result.ExtractedText);
        Assert.True(result.WordCount >= 3);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ExtractAsync_ConteudoVazio_RetornaEmpty()
    {
        var result = await _sut.ExtractAsync("vazio.txt", "text/plain", [], CancellationToken.None);

        Assert.Equal(ExtractionStatus.Empty, result.ExtractionStatus);
        Assert.Null(result.ExtractedText);
    }

    [Fact]
    public async Task ExtractAsync_FormatoDesconhecido_RetornaUnsupportedFormat()
    {
        var content = Encoding.UTF8.GetBytes("dados binarios");

        var result = await _sut.ExtractAsync("arquivo.xyz", "application/unknownformat", content, CancellationToken.None);

        Assert.Equal(ExtractionStatus.UnsupportedFormat, result.ExtractionStatus);
    }

    [Fact]
    public async Task ExtractAsync_TextoComBomUtf8_RemoveBom()
    {
        // BOM UTF-8 = EF BB BF seguido do conteudo
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var text = Encoding.UTF8.GetBytes("Texto com BOM UTF-8.");
        var combined = new byte[bom.Length + text.Length];
        bom.CopyTo(combined, 0);
        text.CopyTo(combined, bom.Length);
        var content = combined;

        var result = await _sut.ExtractAsync("bom.txt", "text/plain", content, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.ExtractionStatus);
        Assert.False(result.ExtractedText!.StartsWith('\uFEFF'));
        Assert.Contains("Texto com BOM UTF-8", result.ExtractedText);
    }

    [Fact]
    public async Task ExtractAsync_HtmlComScript_RemoveScriptTag()
    {
        const string html = "<html><script>alert('xss')</script><body>Conteudo pedagogico.</body></html>";
        var content = Encoding.UTF8.GetBytes(html);

        var result = await _sut.ExtractAsync("resposta.html", "text/html", content, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Succeeded, result.ExtractionStatus);
        Assert.DoesNotContain("alert", result.ExtractedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Conteudo pedagogico", result.ExtractedText);
    }

    private static byte[] CreateMinimalPdf(string text)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount($"BT /F1 24 Tf 100 700 Td ({text}) Tj ET")} >>\nstream\nBT /F1 24 Tf 100 700 Td ({text}) Tj ET\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var builder = new StringBuilder();
        builder.AppendLine("%PDF-1.4");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.AppendLine($"{i + 1} 0 obj");
            builder.AppendLine(objects[i]);
            builder.AppendLine("endobj");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.AppendLine("xref");
        builder.AppendLine($"0 {objects.Length + 1}");
        builder.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
        {
            builder.AppendLine($"{offset:0000000000} 00000 n ");
        }

        builder.AppendLine("trailer");
        builder.AppendLine($"<< /Size {objects.Length + 1} /Root 1 0 R >>");
        builder.AppendLine("startxref");
        builder.AppendLine(xrefOffset.ToString());
        builder.AppendLine("%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
