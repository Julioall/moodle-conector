using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Tools;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Presentation.Tools;

/// <summary>
/// Reconciliation remains available even when the universal write feature is
/// disabled, because specialized grading/message/forum writes can also end in
/// execution_unknown and must never be retried blindly.
/// </summary>
[McpServerToolType]
public sealed class MoodleWriteReconciliationTools(
    IMoodleWriteReconciliationService reconciliationService)
{
    [McpServerTool(Name = "moodle_reconcile_write", Title = "Reconciliar Escrita Moodle",
        ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleWriteReconciliationResult>))]
    [MoodleToolMetadata(Family = "infrastructure", Classification = "R5", Kind = "controlled-write", CanonicalOperation = "moodle_reconcile_write", Structural = false,
        ExposureReason = "Reconcilia uma escrita ambígua sem reenviar a operação remota.")]
    [Description("Resolve uma ação em execution_unknown sem reenviar a requisição ao Moodle. Use resolution='executed' quando houver evidência de aplicação remota ou 'not_applied' quando houver evidência de que não foi aplicada; depois crie uma nova prévia se necessário.")]
    public async Task<CallToolResult> ReconcileWriteAsync(
        [Description("Identificador da ação pendente que está em execution_unknown.")] Guid pendingActionId,
        [Description("Resolução: executed ou not_applied.")] string resolution,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await reconciliationService.ReconcileAsync(pendingActionId, resolution, cancellationToken);
            return Result(data, data.Status == "reconciled"
                ? "A ação foi reconciliada sem nova chamada ao Moodle."
                : "A ação já havia sido reconciliada.", false);
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleWriteReconciliationResult>(ex); }
        catch (ArgumentException ex) { return ToolResultHelper.Error<MoodleWriteReconciliationResult>(ex.Message); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleWriteReconciliationResult>(ex.Message); }
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
