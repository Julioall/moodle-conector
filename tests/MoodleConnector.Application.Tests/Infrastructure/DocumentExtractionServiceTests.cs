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
    public async Task ExtractAsync_Pdf_RetornaUnsupportedFormat()
    {
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF magic bytes

        var result = await _sut.ExtractAsync("relatorio.pdf", "application/pdf", content, CancellationToken.None);

        Assert.Equal(ExtractionStatus.UnsupportedFormat, result.ExtractionStatus);
        Assert.Null(result.ExtractedText);
        Assert.NotEmpty(result.ErrorMessage!);
        Assert.Contains("nao esta disponivel nesta versao", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
}
