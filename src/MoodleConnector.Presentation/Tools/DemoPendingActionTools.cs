using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class DemoPendingActionTools(
    IPendingActionService pendingActions,
    IActionConfirmationService confirmations,
    IOptions<FeatureOptions> features,
    IOptions<PendingActionOptions> pendingActionOptions)
{
    [McpServerTool(
        Name = "prepare_demo_action",
        Title = "Prepare Demo Action",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PrepareDemoActionResponse))]
    [Description("Cria uma acao pendente de demonstracao para validar o fluxo de confirmacao humana antes de qualquer escrita real.")]
    public async Task<CallToolResult> PrepareDemoActionAsync(
        [Description("Texto demonstrativo que sera exibido na pre-visualizacao.")]
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!features.Value.DemoToolsEnabled)
        {
            return Error("demo_tools_disabled", "As tools de demonstracao estao desabilitadas.");
        }

        var confirmationText = $"CONFIRMAR DEMO {message.Trim()}";
        var response = await pendingActions.CreatePendingActionAsync(
            "preparar_acao_demo",
            ToolRiskLevel.HumanConfirmedWrite,
            new { message },
            new { message, effect = "Nenhuma escrita real sera executada." },
            confirmationText,
            TimeSpan.FromMinutes(pendingActionOptions.Value.PendingActionExpirationMinutes),
            courseId: null,
            cancellationToken);

        var structured = new PrepareDemoActionResponse(
            response.Status,
            response.PendingActionId,
            response.ToolName,
            response.RiskLevel.ToString(),
            response.Preview,
            response.ConfirmationText,
            response.ExpiresAt);

        return Success(
            $"Acao demo preparada. Para confirmar, envie exatamente: {response.ConfirmationText}",
            structured);
    }

    [McpServerTool(
        Name = "confirm_demo_action",
        Title = "Confirm Demo Action",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ConfirmDemoActionResponse))]
    [Description("Confirma uma acao pendente de demonstracao usando o texto exato retornado na etapa de preparo.")]
    public async Task<CallToolResult> ConfirmDemoActionAsync(
        [Description("Identificador da acao pendente.")]
        Guid pendingActionId,
        [Description("Texto exato de confirmacao retornado na preparacao.")]
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        if (!features.Value.DemoToolsEnabled)
        {
            return Error("demo_tools_disabled", "As tools de demonstracao estao desabilitadas.");
        }

        var response = await confirmations.ConfirmAsync(
            pendingActionId,
            confirmationText,
            requiredScope: null,
            cancellationToken);

        var structured = new ConfirmDemoActionResponse(
            response.Status,
            response.PendingActionId,
            response.ToolName,
            response.RiskLevel.ToString(),
            response.ConfirmedAt,
            response.AuditId);

        return Success("Acao demo confirmada. Nenhuma escrita real foi executada.", structured);
    }

    private static CallToolResult Success<T>(string text, T structured)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(structured),
            IsError = false
        };
    }

    private static CallToolResult Error(string code, string message)
    {
        var structured = new { status = "error", code, message };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(structured),
            IsError = true
        };
    }

    public sealed record PrepareDemoActionResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
        [property: JsonPropertyName("toolName")] string ToolName,
        [property: JsonPropertyName("riskLevel")] string RiskLevel,
        [property: JsonPropertyName("preview")] object Preview,
        [property: JsonPropertyName("confirmationText")] string ConfirmationText,
        [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);

    public sealed record ConfirmDemoActionResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
        [property: JsonPropertyName("toolName")] string ToolName,
        [property: JsonPropertyName("riskLevel")] string RiskLevel,
        [property: JsonPropertyName("confirmedAt")] DateTimeOffset ConfirmedAt,
        [property: JsonPropertyName("auditId")] string? AuditId);
}
