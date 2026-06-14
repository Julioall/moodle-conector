using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Grading;

[McpServerToolType]
public sealed class MoodleGradingContextDiagnosticsTools(IMediator mediator)
{
    [McpServerTool(
        Name = "consultar_contexto_item_correcao_assistida",
        Title = "Consultar Contexto Item Correcao Assistida",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AssistedGradingContextDiagnosticsResult>))]
    [Description("Consulta diagnosticos sanitizados dos artefatos assignment_context vinculados a um item de correcao assistida. Nao retorna o conteudo integral dos documentos e nao escreve no Moodle.")]
    public async Task<CallToolResult> ConsultarContextoItemCorrecaoAssistidaAsync(
        [Description("Identificador do item retornado pelo status do lote.")]
        Guid gradingItemId,
        [Description("Identificador opcional do lote esperado para validar vinculo do item.")]
        Guid? batchJobId = null,
        CancellationToken cancellationToken = default)
    {
        if (gradingItemId == Guid.Empty)
        {
            return Error("Informe um identificador de item valido.");
        }

        AssistedGradingContextDiagnosticsResult data;
        try
        {
            data = await mediator.Send(
                new GetAssistedGradingContextDiagnosticsQuery(gradingItemId, batchJobId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error(ex.Message);
        }
        catch
        {
            return Error("Nao foi possivel consultar o contexto do item de correcao assistida neste momento.");
        }

        var response = new ToolResponse<AssistedGradingContextDiagnosticsResult>(
            "ok",
            data,
            data.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static CallToolResult Error(string message)
    {
        var response = new ToolResponse<AssistedGradingContextDiagnosticsResult>(
            "error",
            Data: null,
            Warnings: [message],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }

    private static string BuildNarration(AssistedGradingContextDiagnosticsResult data)
    {
        var selected = string.IsNullOrWhiteSpace(data.SelectedContextFileName)
            ? "nenhum contexto selecionado"
            : $"selecionado {data.SelectedContextFileName}";
        var source = string.IsNullOrWhiteSpace(data.SelectedAssignmentStatementSource)
            ? "fonte de enunciado nao confirmada"
            : $"enunciado provavel: {data.SelectedAssignmentStatementSource}";

        return string.Join('\n',
        [
            $"Diagnostico de contexto do item {data.GradingItemId}.",
            $"Artefatos assignment_context: {data.AssignmentContextArtifactsCount}; extraidos com texto: {data.AssignmentContextExtractedArtifactsCount}.",
            $"Selecao heuristica: {selected}; classificacao: {data.SelectedContextClassification ?? "none"}; score: {FormatDecimal(data.SelectedContextScore)}; confianca: {FormatDecimal(data.SelectedContextConfidence)}.",
            $"{source}; caracteres extraidos do contexto selecionado: {data.ExtractedContextChars}; palavras: {data.ExtractedContextWords}.",
            $"Materiais selecionados/suporte: {(data.SelectedCourseMaterials.Count == 0 ? "nenhum" : string.Join(", ", data.SelectedCourseMaterials))}."
        ]);
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
    }
}
