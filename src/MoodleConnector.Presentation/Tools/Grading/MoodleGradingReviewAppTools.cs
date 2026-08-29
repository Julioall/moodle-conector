using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Presentation.Tools.Grading;

[McpServerToolType]
public sealed class MoodleGradingReviewAppTools(
    IMediator mediator,
    IGradingOperationTelemetry? telemetry = null)
{
    [McpServerTool(
        Name = "review_batch_feedbacks",
        Title = "Review Batch Feedbacks",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingReviewAppData>))]
    [Description("Retorna uma interface interativa para revisar, editar e confirmar feedbacks de um lote de correção assistida. A interface preserva o estado visual, diferencia os estados do fluxo e sempre reconcilia alterações com o estado persistido no servidor.")]
    public async Task<CallToolResult> RevisarFeedbacksLoteAsync(
        [Description("Identificador do lote retornado por criar_lote_correcao_assistida.")]
        Guid batchJobId,
        [Description("Página de itens, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da página, de 1 a 100.")]
        int tamanhoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        if (batchJobId == Guid.Empty)
        {
            return Error("Informe um identificador de lote válido.");
        }

        GradingReviewPageReadModel page;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            page = await mediator.Send(
                new GetGradingReviewPageQuery(batchJobId, pagina, tamanhoPagina),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            telemetry?.RecordPhase("review", "read_model", "error", stopwatch.Elapsed.TotalMilliseconds);
            return Error("Não foi possível consultar o lote de correção assistida neste momento.");
        }
        telemetry?.RecordPhase("review", "read_model", "success", stopwatch.Elapsed.TotalMilliseconds, page.QueryCount, page.Items.Count);

        var enrichedItems = page.Items.Select(item =>
        {
            var workflowState = ResolveWorkflowState(item.Status, item.ReviewStatus, item.CommitStatus);
            var capabilities = ResolveCapabilities(workflowState);
            return new GradingReviewItem(
                item.GradingItemId, item.AssignmentId, item.SubmissionId, item.StudentId,
                StudentName: item.StudentName, item.Status, item.ReviewStatus, item.CommitStatus,
                workflowState, capabilities.CanEdit, capabilities.CanSelect, capabilities.CanSend,
                item.StatusReason, item.DraftVersionHash, item.FinalGrade, item.FinalFeedback,
                item.SuggestedGrade, item.DraftFeedback, item.MaxGrade,
                item.GradingMode switch
                {
                    "numeric" => true,
                    "feedback_only" => false,
                    _ => null
                },
                item.AssignmentName, item.Confidence,
                item.ContextHash, item.GradingMode, item.Warnings, item.Coverage);
        }).ToArray();

        var appData = new GradingReviewAppData(
            batchJobId,
            page.Status,
            page.TotalItems,
            page.ReadyItems,
            page.BlockedItems,
            page.FailedItems,
            page.ProgressPercent,
            page.Page,
            page.PageSize,
            page.HasMore,
            enrichedItems,
            CourseName: page.CourseName,
            DataSource: page.DataSource,
            ReadModelVersion: page.ReadModelVersion,
            QueryCount: page.QueryCount);

        var response = new ToolResponse<GradingReviewAppData>(
            "ok",
            appData,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildReviewNarration(appData) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            Meta = MoodleGradingReviewAppMetadata.CreateToolMeta(),
            IsError = false
        };
    }

    [McpServerTool(
        Name = "get_batch_grading_ui_state",
        Title = "Get Batch Grading UI State",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingReviewAppData>))]
    [Description("Returns the paginated authoritative snapshot used by the review interface, without requesting a widget re-render.")]
    public async Task<CallToolResult> ConsultarEstadoInterfaceCorrecaoLoteAsync(
        [Description("Identificador do lote de correção assistida.")] Guid batchJobId,
        [Description("Página de itens, iniciando em 1.")] int pagina = 1,
        [Description("Tamanho da página, de 1 a 100.")] int tamanhoPagina = 50,
        CancellationToken cancellationToken = default)
    {
        var rendered = await RevisarFeedbacksLoteAsync(
            batchJobId,
            pagina,
            tamanhoPagina,
            cancellationToken);

        return new CallToolResult
        {
            Content = rendered.Content,
            StructuredContent = rendered.StructuredContent,
            IsError = rendered.IsError
        };
    }

    private static string ResolveWorkflowState(
        string status,
        string reviewStatus,
        string commitStatus)
    {
        if (EqualsStatus(commitStatus, "Succeeded") || EqualsStatus(status, "Committed"))
        {
            return GradingReviewWorkflowStates.Sent;
        }

        if (EqualsStatus(commitStatus, "Failed"))
        {
            return GradingReviewWorkflowStates.SendFailed;
        }

        if (EqualsStatus(status, "Blocked"))
        {
            return GradingReviewWorkflowStates.Blocked;
        }

        if (EqualsStatus(status, "Failed"))
        {
            return GradingReviewWorkflowStates.AnalysisFailed;
        }

        if (EqualsStatus(status, "Pending") ||
            EqualsStatus(status, "Analyzing") ||
            EqualsStatus(status, "AwaitingAiAnalysis"))
        {
            return GradingReviewWorkflowStates.Processing;
        }

        if (EqualsStatus(status, "ReadyToCommit") &&
            EqualsStatus(reviewStatus, "Reviewed") &&
            EqualsStatus(commitStatus, "Pending"))
        {
            return GradingReviewWorkflowStates.Reviewed;
        }

        return GradingReviewWorkflowStates.AwaitingReview;
    }

    private static (bool CanEdit, bool CanSelect, bool CanSend) ResolveCapabilities(string workflowState)
    {
        return workflowState switch
        {
            GradingReviewWorkflowStates.AwaitingReview => (true, true, false),
            GradingReviewWorkflowStates.Reviewed => (true, true, true),
            GradingReviewWorkflowStates.SendFailed => (true, true, false),
            _ => (false, false, false)
        };
    }

    private static bool EqualsStatus(string value, string expected)
    {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildReviewNarration(GradingReviewAppData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Revisão de Correções");
        sb.AppendLine();
        sb.AppendLine($"**{data.Items.Count} aluno(s)** nesta página, de **{data.TotalItems}** no lote.");
        sb.AppendLine();

        if (data.Items.Count == 0)
        {
            sb.AppendLine("O lote foi carregado e não possui correções nesta página.");
            return sb.ToString();
        }

        foreach (var item in data.Items)
        {
            var displayName = !string.IsNullOrWhiteSpace(item.StudentName)
                ? item.StudentName
                : $"Aluno {item.StudentId}";
            var displayGrade = item.FinalGrade ?? item.SuggestedGrade;
            var displayFeedback = !string.IsNullOrWhiteSpace(item.FinalFeedback)
                ? item.FinalFeedback
                : item.DraftFeedback;
            var grade = displayGrade.HasValue
                ? item.MaxGrade is > 0
                    ? $"{displayGrade.Value:F1}/{item.MaxGrade.Value:F0}"
                    : $"{displayGrade.Value:F1}/?"
                : "—";

            sb.AppendLine("---");
            sb.AppendLine($"### {displayName} — {WorkflowLabel(item.WorkflowState)} — {grade}");
            if (!string.IsNullOrWhiteSpace(item.AssignmentName))
            {
                sb.AppendLine($"*{item.AssignmentName}*");
            }

            if (!string.IsNullOrWhiteSpace(displayFeedback))
            {
                sb.AppendLine();
                sb.AppendLine("**Feedback:**");
                sb.AppendLine($"> {displayFeedback.Replace("\n", "\n> ")}");
            }

            if (!string.IsNullOrWhiteSpace(item.StatusReason))
            {
                sb.AppendLine();
                sb.AppendLine($"**Atenção:** {item.StatusReason}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("A interface preserva seleções e edições temporárias, enquanto os estados acadêmicos permanecem persistidos no servidor.");
        return sb.ToString();
    }

    private static string WorkflowLabel(string workflowState)
    {
        return workflowState switch
        {
            GradingReviewWorkflowStates.Processing => "Processando",
            GradingReviewWorkflowStates.AwaitingReview => "Aguardando revisão",
            GradingReviewWorkflowStates.Reviewed => "Corrigido",
            GradingReviewWorkflowStates.Sent => "Enviado",
            GradingReviewWorkflowStates.Blocked => "Bloqueado",
            GradingReviewWorkflowStates.AnalysisFailed => "Falha na análise",
            GradingReviewWorkflowStates.SendFailed => "Falha no envio",
            _ => workflowState
        };
    }

    private static CallToolResult Error(string message)
    {
        var response = new ToolResponse<GradingReviewAppData>(
            "error",
            Data: default,
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
}

[McpServerResourceType]
public sealed class MoodleGradingReviewAppResources
{
    [McpServerResource(
        UriTemplate = MoodleGradingReviewAppMetadata.ResourceUri,
        Name = "grading-review-app-v2",
        Title = "Interface de Revisão de Feedbacks",
        MimeType = MoodleGradingReviewAppMetadata.ResourceMimeType)]
    [Description("Interface HTML interativa para revisar feedbacks de correção assistida.")]
    public IEnumerable<ResourceContents> GetReviewApp()
    {
        yield return new TextResourceContents
        {
            Uri = MoodleGradingReviewAppMetadata.ResourceUri,
            MimeType = MoodleGradingReviewAppMetadata.ResourceMimeType,
            Text = LoadResourceHtml(),
            Meta = MoodleGradingReviewAppMetadata.CreateResourceMeta()
        };
    }

    private static string LoadResourceHtml()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("GradingReviewApp.html", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? ".";
        var htmlPath = Path.Combine(assemblyDir, "Tools", "Grading", "GradingReviewApp.html");
        return File.Exists(htmlPath)
            ? File.ReadAllText(htmlPath, Encoding.UTF8)
            : "<html><body>Template não encontrado</body></html>";
    }
}

public static class MoodleGradingReviewAppMetadata
{
    public const string ToolName = "review_batch_feedbacks";
    public const string ResourceUri = "ui://grading-review/v2/app.html";
    public const string ResourceMimeType = "text/html;profile=mcp-app";

    public static JsonObject CreateToolMeta()
    {
        return new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["resourceUri"] = ResourceUri
            },
            ["openai/outputTemplate"] = ResourceUri,
            ["openai/toolInvocation/invoking"] = "Carregando correções…",
            ["openai/toolInvocation/invoked"] = "Correções carregadas."
        };
    }

    public static JsonObject CreateResourceMeta()
    {
        var domain = ResolveWidgetDomain();
        return new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["prefersBorder"] = true,
                ["domain"] = domain,
                ["csp"] = new JsonObject
                {
                    ["connectDomains"] = new JsonArray(),
                    ["resourceDomains"] = new JsonArray()
                }
            },
            ["openai/widgetDescription"] = "Interface para revisar feedbacks de correção assistida e acionar tools de confirmação humana.",
            ["openai/widgetPrefersBorder"] = true,
            ["openai/widgetDomain"] = domain,
            ["openai/widgetCSP"] = new JsonObject
            {
                ["connect_domains"] = new JsonArray(),
                ["resource_domains"] = new JsonArray()
            }
        };
    }

    private static string ResolveWidgetDomain()
    {
        var configured = Environment.GetEnvironmentVariable("APP_DOMAIN");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return "https://localhost";
        }

        configured = configured.Trim().TrimEnd('/');
        if (!configured.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !configured.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            configured = $"https://{configured}";
        }

        return Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : "https://localhost";
    }
}

public static class GradingReviewWorkflowStates
{
    public const string Processing = "processing";
    public const string AwaitingReview = "awaiting_review";
    public const string Reviewed = "reviewed";
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Blocked = "blocked";
    public const string AnalysisFailed = "analysis_failed";
    public const string SendFailed = "send_failed";
}

public sealed record GradingReviewAppData(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("readyItems")] int ReadyItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("progressPercent")] int ProgressPercent,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("hasMore")] bool HasMore,
    [property: JsonPropertyName("items")] IReadOnlyList<GradingReviewItem> Items,
    [property: JsonPropertyName("courseName")] string? CourseName = null,
    [property: JsonPropertyName("dataSource")] string DataSource = "local_read_model",
    [property: JsonPropertyName("readModelVersion")] string ReadModelVersion = "1",
    [property: JsonPropertyName("queryCount")] int? QueryCount = null);

public sealed record GradingReviewItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("studentName")] string? StudentName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("commitStatus")] string CommitStatus,
    [property: JsonPropertyName("workflowState")] string WorkflowState,
    [property: JsonPropertyName("canEdit")] bool CanEdit,
    [property: JsonPropertyName("canSelect")] bool CanSelect,
    [property: JsonPropertyName("canSend")] bool CanSend,
    [property: JsonPropertyName("statusReason")] string? StatusReason,
    [property: JsonPropertyName("draftVersionHash")] string? DraftVersionHash,
    [property: JsonPropertyName("finalGrade")] decimal? FinalGrade,
    [property: JsonPropertyName("finalFeedback")] string? FinalFeedback,
    [property: JsonPropertyName("suggestedGrade")] decimal? SuggestedGrade,
    [property: JsonPropertyName("draftFeedback")] string? DraftFeedback,
    [property: JsonPropertyName("maxGrade")] decimal? MaxGrade,
    [property: JsonPropertyName("isGradable")] bool? IsGradable,
    [property: JsonPropertyName("assignmentName")] string? AssignmentName,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("contextHash")] string? ContextHash = null,
    [property: JsonPropertyName("gradingMode")] string GradingMode = "unknown",
    [property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings = null,
    [property: JsonPropertyName("coverage")] GradingEvidenceCoverage? Coverage = null);
