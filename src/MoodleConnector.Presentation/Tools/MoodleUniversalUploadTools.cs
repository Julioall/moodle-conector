using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Tools;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleUniversalUploadTools(
    IMoodleUniversalUploadService uploadService,
    IMoodleConnectionSelection connectionSelection)
{
    [McpServerTool(Name = "moodle_prepare_upload", Title = "Preparar Upload Moodle",
        ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleUploadPreview>))]
    [MoodleToolMetadata(
        Family = "files",
        Classification = "R5",
        Kind = "controlled-write",
        CanonicalOperation = "moodle_upload_draft",
        ExposureStatus = "Keep",
        ExposureReason = "Upload binario para area de rascunho Moodle, sempre precedido por previa e confirmacao.",
        Evidence = "Usa resource Moodle autorizado, preserva hash/tamanho e nao aceita base64 nem executa upload na preparacao.",
        RequiredPlatformPermission = "tool.files.write")]
    [Description("Prepara o envio de um resource Moodle existente para a area de rascunho. Use uma URI moodle://resource/... autorizada; o conteudo binario nao deve ser convertido para base64. Nenhuma chamada de upload e feita nesta etapa.")]
    public async Task<CallToolResult> PrepareUploadAsync(
        [Description("URI opaca de um resource Moodle autorizado, normalmente moodle://resource/{id}.")] string resourceUri,
        [Description("Nome do arquivo no rascunho; se omitido, usa o nome do resource.")] string? filename = null,
        [Description("MIME do arquivo; se omitido, usa o MIME observado no resource.")] string? mimeType = null,
        [Description("Caminho do arquivo no rascunho Moodle.")] string filePath = "/",
        [Description("itemid existente para anexar ao mesmo rascunho, quando aplicável.")] int? itemId = null,
        [Description("Alias opcional da conexão Moodle de destino.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            return ToolResultHelper.Error<MoodleUploadPreview>("Informe a URI do resource Moodle.");
        }

        connectionSelection.Alias = moodleAlias;
        try
        {
            var data = await uploadService.PrepareAsync(resourceUri, filename, mimeType, filePath, itemId, cancellationToken);
            return Result(data, $"Upload de '{data.Filename}' preparado. Revise e confirme usando o texto literal informado.", false);
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleUploadPreview>(ex); }
        catch (ArgumentException ex) { return ToolResultHelper.Error<MoodleUploadPreview>(ex.Message); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleUploadPreview>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_confirm_upload", Title = "Confirmar Upload Moodle",
        ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleUploadResult>))]
    [MoodleToolMetadata(
        Family = "files",
        Classification = "R5",
        Kind = "controlled-write",
        CanonicalOperation = "moodle_upload_draft_confirm",
        ExposureStatus = "Keep",
        ExposureReason = "Executa uma unica transferencia binaria confirmada e devolve itemid para uma escrita posterior.",
        Evidence = "A confirmacao e atomica por pending action; timeout permanece execution_unknown e nao gera reenvio automatico.",
        RequiredPlatformPermission = "tool.files.write")]
    [Description("Confirma uma unica vez o upload Moodle preparado. Retorna o itemid do rascunho quando o Moodle o fornecer; anexar o rascunho a uma atividade continua sendo uma segunda escrita.")]
    public async Task<CallToolResult> ConfirmUploadAsync(
        Guid pendingActionId,
        string confirmationText,
        [Description("Alias da mesma conexão usada na preparação.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        connectionSelection.Alias = moodleAlias;
        try
        {
            var data = await uploadService.ConfirmAsync(pendingActionId, confirmationText, cancellationToken);
            var isError = data.Status is "upload_failed" or "execution_unknown";
            var message = data.Status switch
            {
                "executed" => $"Upload de rascunho executado para '{data.Files.FirstOrDefault()?.Filename ?? "arquivo"}'.",
                "execution_unknown" => "O upload ficou com resultado desconhecido. Não repita o envio; reconcilie a ação antes de criar nova prévia.",
                _ => "O upload já havia sido confirmado e não foi repetido."
            };
            return Result(data, message, isError);
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleUploadResult>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleUploadResult>(ex.Message); }
    }

    private static CallToolResult Result<T>(T data, string narration, bool isError)
    {
        var response = new ToolResponse<T>(isError ? "error" : "ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = isError
        };
    }
}
