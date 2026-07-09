using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Memory;
using MoodleConnector.Application.Tools;
using MoodleConnector.Presentation.Tools.Memory;

namespace MoodleConnector.Application.Tests.Tools.Memory;

public sealed class MoodleMemoryDocumentToolsTests
{
    [Fact]
    public async Task Salvar_delega_documento_e_retorna_conteudo_completo()
    {
        var service = new FakeDocumentService();
        var sut = new MoodleMemoryDocumentTools(service);

        var result = await sut.ManageAsync("salvar", key: "cronograma", title: "Cronograma", content: "<table>...</table>", format: "html", origin: "explicit", moodleAlias: "senai", courseId: "42");

        Assert.False(result.IsError ?? false);
        Assert.Equal(new SaveUserMemoryDocumentRequest("cronograma", "Cronograma", "<table>...</table>", "html", "explicit", "senai", "42"), service.SaveRequest);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.Equal("salvar", data.GetProperty("action").GetString());
        Assert.Equal(service.Document.Id, data.GetProperty("document").GetProperty("id").GetGuid());
        Assert.Equal("<table>...</table>", data.GetProperty("document").GetProperty("content").GetString());
    }

    [Fact]
    public async Task Ler_exige_document_id_e_delega_ao_servico()
    {
        var service = new FakeDocumentService();
        var sut = new MoodleMemoryDocumentTools(service);

        var result = await sut.ManageAsync("ler", documentId: service.Document.Id);

        Assert.False(result.IsError ?? false);
        Assert.Equal(service.Document.Id, service.ReadId);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.Equal("ler", data.GetProperty("action").GetString());
        Assert.Equal("markdown", data.GetProperty("document").GetProperty("format").GetString());
    }

    [Fact]
    public async Task Listar_delega_filtros_e_nao_exige_conteudo()
    {
        var service = new FakeDocumentService();
        var sut = new MoodleMemoryDocumentTools(service);

        var result = await sut.ManageAsync("listar", query: "cronograma", moodleAlias: "senai", courseId: "42", limit: 5);

        Assert.False(result.IsError ?? false);
        Assert.Equal(new ListUserMemoryDocumentsRequest("senai", "42", 5, "cronograma"), service.ListRequest);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.Equal(1, data.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task Remover_exige_document_id_e_delega_ao_servico()
    {
        var service = new FakeDocumentService();
        var sut = new MoodleMemoryDocumentTools(service);

        var result = await sut.ManageAsync("remover", documentId: service.Document.Id);

        Assert.False(result.IsError ?? false);
        Assert.Equal(service.Document.Id, service.RemovedId);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.True(data.GetProperty("removed").GetBoolean());
    }

    [Fact]
    public void Metadata_declara_escrita_interna_idempotente()
    {
        var method = typeof(MoodleMemoryDocumentTools).GetMethod(nameof(MoodleMemoryDocumentTools.ManageAsync))!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

        Assert.False(attribute.ReadOnly);
        Assert.True(attribute.Destructive);
        Assert.True(attribute.Idempotent);
        Assert.False(attribute.OpenWorld);
        Assert.Equal(typeof(ToolResponse<MemoryDocumentToolResponse>), attribute.OutputSchemaType);
    }

    [Theory]
    [InlineData("salvar")]
    [InlineData("ler")]
    [InlineData("remover")]
    [InlineData("x")]
    public async Task Validacao_retorna_erro_controlado(string action)
    {
        var sut = new MoodleMemoryDocumentTools(new FakeDocumentService());

        var result = await sut.ManageAsync(action);

        Assert.True(result.IsError ?? false);
        var warning = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("warnings")[0].GetString();
        Assert.False(string.IsNullOrWhiteSpace(warning));
        Assert.DoesNotContain(" at ", warning, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDocumentService : IUserMemoryDocumentService
    {
        public UserMemoryDocumentDto Document { get; } = new(Guid.NewGuid(), "cronograma", "Cronograma", "# Cronograma", "markdown", "explicit", "senai", "42", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public SaveUserMemoryDocumentRequest? SaveRequest { get; private set; }
        public ListUserMemoryDocumentsRequest? ListRequest { get; private set; }
        public Guid? ReadId { get; private set; }
        public Guid? RemovedId { get; private set; }

        public Task<UserMemoryDocumentDto> SaveAsync(SaveUserMemoryDocumentRequest request, CancellationToken cancellationToken = default)
        {
            SaveRequest = request;
            return Task.FromResult(Document with
            {
                Title = request.Title,
                Content = request.Content,
                Format = request.Format,
                Origin = request.Origin,
                MoodleAlias = request.MoodleAlias,
                CourseId = request.CourseId
            });
        }

        public Task<UserMemoryDocumentDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ReadId = id;
            return Task.FromResult<UserMemoryDocumentDto?>(Document);
        }

        public Task<IReadOnlyList<UserMemoryDocumentDto>> ListAsync(ListUserMemoryDocumentsRequest request, CancellationToken cancellationToken = default)
        {
            ListRequest = request;
            return Task.FromResult<IReadOnlyList<UserMemoryDocumentDto>>([Document]);
        }

        public Task<RemoveUserMemoryDocumentResult> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RemovedId = id;
            return Task.FromResult(new RemoveUserMemoryDocumentResult(true));
        }
    }
}
