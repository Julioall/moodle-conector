using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Memory;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Memory;

[McpServerToolType]
public sealed class MoodleMemoryDocumentTools(IUserMemoryDocumentService documentService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(
        Name = "gerenciar_documento_memoria_usuario",
        Title = "Gerenciar documento de memoria do usuario",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<MemoryDocumentToolResponse>))]
    [Description("Salva, lista, le ou remove documentos duraveis privados do usuario autenticado para uso como modelos ou referencias extensas da IA. Nunca envie segredos nem dados pessoais de alunos.")]
    public async Task<CallToolResult> ManageAsync(
        [Description("Acao: salvar, listar, ler ou remover.")] string action,
        [Description("Chave curta e estavel do documento. Obrigatoria em salvar.")] string? key = null,
        [Description("Titulo humano do documento. Obrigatorio em salvar.")] string? title = null,
        [Description("Conteudo completo do documento. Use markdown quando possivel; html e aceito para modelos Moodle existentes.")] string? content = null,
        [Description("Formato do conteudo: markdown, html ou text. Obrigatorio em salvar.")] string? format = null,
        [Description("Origem: explicit ou inferred. Obrigatoria em salvar.")] string? origin = null,
        [Description("Texto para filtrar documentos na listagem.")] string? query = null,
        [Description("Alias Moodle opcional para escopo ou filtro.")] string? moodleAlias = null,
        [Description("Curso opcional dentro do alias Moodle.")] string? courseId = null,
        [Description("UUID do documento exigido para ler ou remover.")] Guid? documentId = null,
        [Description("Maximo de documentos na listagem. Padrao: 20.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = (action ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "salvar" => new MemoryDocumentToolResponse("salvar", await SaveAsync(key, title, content, format, origin, moodleAlias, courseId, cancellationToken), null, null),
                "listar" => new MemoryDocumentToolResponse("listar", null, await documentService.ListAsync(new ListUserMemoryDocumentsRequest(moodleAlias, courseId, limit, query), cancellationToken), null),
                "ler" => new MemoryDocumentToolResponse("ler", await ReadAsync(documentId, cancellationToken), null, null),
                "remover" => new MemoryDocumentToolResponse("remover", null, null, (await RemoveAsync(documentId, cancellationToken)).Removed),
                _ => throw new ArgumentException("Acao invalida. Use salvar, listar, ler ou remover.", nameof(action))
            };

            var response = new ToolResponse<MemoryDocumentToolResponse>("ok", data, [], null, DateTimeOffset.UtcNow);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(data, JsonOptions) }],
                StructuredContent = JsonSerializer.SerializeToElement(response, JsonOptions),
                IsError = false
            };
        }
        catch (ArgumentException exception)
        {
            return ToolResultHelper.Error<MemoryDocumentToolResponse>(exception.Message);
        }
    }

    private Task<UserMemoryDocumentDto> SaveAsync(
        string? key,
        string? title,
        string? content,
        string? format,
        string? origin,
        string? moodleAlias,
        string? courseId,
        CancellationToken cancellationToken)
    {
        Require(key, nameof(key));
        Require(title, nameof(title));
        Require(content, nameof(content));
        Require(format, nameof(format));
        Require(origin, nameof(origin));
        return documentService.SaveAsync(new SaveUserMemoryDocumentRequest(key!, title!, content!, format!, origin!, moodleAlias, courseId), cancellationToken);
    }

    private async Task<UserMemoryDocumentDto> ReadAsync(Guid? documentId, CancellationToken cancellationToken)
    {
        var id = RequireDocumentId(documentId);
        return await documentService.ReadAsync(id, cancellationToken)
            ?? throw new ArgumentException("Documento de memoria nao encontrado.", nameof(documentId));
    }

    private Task<RemoveUserMemoryDocumentResult> RemoveAsync(Guid? documentId, CancellationToken cancellationToken)
    {
        return documentService.RemoveAsync(RequireDocumentId(documentId), cancellationToken);
    }

    private static Guid RequireDocumentId(Guid? documentId)
    {
        if (documentId is null || documentId == Guid.Empty)
            throw new ArgumentException("Informe um documentId UUID valido.", nameof(documentId));
        return documentId.Value;
    }

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Informe {parameterName} para salvar.", parameterName);
    }
}

public sealed record MemoryDocumentToolResponse(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("document")] UserMemoryDocumentDto? Document,
    [property: JsonPropertyName("documents")] IReadOnlyList<UserMemoryDocumentDto>? Documents,
    [property: JsonPropertyName("removed")] bool? Removed);
