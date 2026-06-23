using System.ComponentModel;
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
using MoodleConnector.Application.Participants;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Presentation.Tools.Grading;

// ============================================================
// MCP Tool: revisar_feedbacks_lote
// ============================================================

[McpServerToolType]
public sealed class MoodleGradingReviewAppTools(
    IMediator mediator,
    IMoodleCoursesGateway coursesGateway,
    ICurrentUserContext currentUser)
{
    [McpServerTool(
        Name = "revisar_feedbacks_lote",
        Title = "Revisar Feedbacks Lote",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingReviewAppData>))]
    [Description("Retorna uma interface interativa para revisar, editar e confirmar feedbacks de um lote de correção assistida. A interface permite visualizar feedbacks, editar nota e feedback por aluno, e enviar ao Moodle.")]
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

        AssistedGradingBatchStatusResult batchStatus;
        try
        {
            batchStatus = await mediator.Send(
                new GetAssistedGradingBatchStatusQuery(batchJobId, pagina, tamanhoPagina),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error("Não foi possível consultar o lote de correção assistida neste momento.");
        }

        // ── Phase 1: Resolve course name + student names (non-critical, single pass) ─────
        var studentNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? resolvedCourseName = null;

        // Cache the first item's detail result so it is not fetched twice in Phase 2
        var detailCache = new Dictionary<Guid, AssistedGradingItemDetailResult>();

        if (batchStatus.Items.Count > 0)
        {
            var firstItem = batchStatus.Items[0];
            AssistedGradingItemDetailResult? firstDetail = null;
            try
            {
                firstDetail = await mediator.Send(
                    new GetAssistedGradingItemQuery(firstItem.GradingItemId, batchJobId),
                    cancellationToken);
                detailCache[firstItem.GradingItemId] = firstDetail;
            }
            catch { /* non-critical */ }

            var courseId = firstDetail?.CourseId;
            if (!string.IsNullOrWhiteSpace(courseId))
            {
                try
                {
                    var course = await coursesGateway.GetMyCourseAsync(
                        currentUser.Subject, courseId, cancellationToken);
                    resolvedCourseName = course?.FullName ?? course?.DisplayName;
                }
                catch { /* non-critical */ }

                try
                {
                    var participantsPage = await mediator.Send(
                        new ListCourseParticipantsQuery(
                            currentUser.Subject,
                            courseId,
                            ParticipantStatusFilter.All,
                            Page: 1,
                            PageSize: 50,
                            StudentsOnly: true,
                            IncludeEmail: false),
                        cancellationToken);
                    if (participantsPage is not null)
                    {
                        foreach (var p in participantsPage.Participants)
                        {
                            studentNameMap[p.UserId] = p.FullName;
                        }
                    }
                }
                catch { /* non-critical: fallback to "Aluno {id}" */ }
            }
        }

        // ── Phase 2: Enrich each item (feedback, grade, maxGrade, assignmentName) ─────────
        var enrichedItems = new List<GradingReviewItem>();
        foreach (var item in batchStatus.Items)
        {
            // Reuse cached detail from Phase 1 (avoids double-fetching for item[0])
            if (!detailCache.TryGetValue(item.GradingItemId, out var detail))
            {
                try
                {
                    detail = await mediator.Send(
                        new GetAssistedGradingItemQuery(item.GradingItemId, batchJobId),
                        cancellationToken);
                }
                catch { /* non-critical: fall back to context query */ }
            }

            GradingContextForChatResult? context = null;
            try
            {
                context = await mediator.Send(
                    new PrepareGradingContextForChatQuery(item.GradingItemId, batchJobId),
                    cancellationToken);
            }
            catch { /* non-critical */ }

            var finalGrade    = detail?.FinalGrade;
            var finalFeedback = !string.IsNullOrWhiteSpace(detail?.FinalFeedback) ? detail!.FinalFeedback : null;
            var suggestedGrade = detail?.SuggestedGrade ?? context?.SuggestedGrade;
            var draftFeedback  = detail?.DraftFeedback  ?? context?.DraftFeedback;
            var maxGrade       = context?.MaxGrade       ?? 100m;
            var assignmentName = context?.AssignmentName;
            var confidence     = detail?.Confidence      ?? context?.Confidence;

            studentNameMap.TryGetValue(item.StudentId, out var studentName);

            // Lazy fallback: resolve course name from context if Phase 1 didn't get it
            if (resolvedCourseName is null && context is not null)
            {
                try
                {
                    var course = await coursesGateway.GetMyCourseAsync(
                        currentUser.Subject, context.CourseId, cancellationToken);
                    resolvedCourseName = course?.FullName ?? course?.DisplayName;
                }
                catch { /* non-critical */ }
            }

            enrichedItems.Add(new GradingReviewItem(
                item.GradingItemId,
                item.AssignmentId,
                item.SubmissionId,
                item.StudentId,
                StudentName: studentName,
                item.Status,
                item.ReviewStatus,
                item.CommitStatus,
                finalGrade,
                finalFeedback,
                suggestedGrade,
                draftFeedback,
                maxGrade,
                assignmentName,
                confidence));
        }

        var appData = new GradingReviewAppData(
            batchJobId,
            batchStatus.Status,
            batchStatus.TotalItems,
            batchStatus.ReadyItems,
            batchStatus.BlockedItems,
            batchStatus.FailedItems,
            batchStatus.ProcessingMetrics.ProgressPercent,
            enrichedItems,
            resolvedCourseName);

        // Build structured content for fallback (hosts without MCP Apps)
        var narration = BuildReviewNarration(appData);

        var response = new ToolResponse<GradingReviewAppData>(
            "ok",
            appData,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = narration }
            ],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            Meta = MoodleGradingReviewAppMetadata.CreateToolMeta(),
            IsError = false
        };

        return result;
    }

    // ============================================================
    // HTML Template (used by MCP Resource only; data is delivered via StructuredContent)
    // ============================================================

    private static string LoadHtmlTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("GradingReviewApp.html", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            // Fallback: load from file system relative to assembly
            var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? ".";
            var htmlPath = Path.Combine(assemblyDir, "Tools", "Grading", "GradingReviewApp.html");
            if (File.Exists(htmlPath))
            {
                return File.ReadAllText(htmlPath, Encoding.UTF8);
            }

            return BuildMinimalFallbackHtml();
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string BuildMinimalFallbackHtml()
    {
        return """
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head><meta charset="UTF-8"><title>Revisão</title></head>
            <body style="background:#0f172a;color:#f1f5f9;font-family:system-ui;padding:24px">
              <h2>Interface de Revisão não disponível</h2>
              <p>O template HTML não foi encontrado. Use as tools MCP diretamente para revisar os feedbacks.</p>
            </body>
            </html>
            """;
    }

    // ============================================================
    // Narration (fallback for hosts without MCP Apps)
    // ============================================================

    private static string BuildReviewNarration(GradingReviewAppData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Revisão de Correções");
        sb.AppendLine();
        sb.AppendLine($"**{data.Items.Count} aluno(s)** para revisão.");
        sb.AppendLine();

        if (data.Items.Count == 0)
        {
            sb.AppendLine("Nenhuma correção disponível para revisão.");
            return sb.ToString();
        }

        foreach (var item in data.Items)
        {
            var displayName = !string.IsNullOrWhiteSpace(item.StudentName)
                ? item.StudentName
                : $"Aluno {item.StudentId}";

            // Prioritize final over suggested
            var displayGrade = item.FinalGrade ?? item.SuggestedGrade;
            var displayFeedback = !string.IsNullOrWhiteSpace(item.FinalFeedback)
                ? item.FinalFeedback
                : item.DraftFeedback;

            var gradeStr = displayGrade.HasValue
                ? $"{displayGrade.Value:F1}/{item.MaxGrade:F0}"
                : "—";

            var gradeLabel = item.FinalGrade.HasValue ? "Nota final" : "Nota sugerida";

            sb.AppendLine($"---");
            sb.AppendLine($"### {displayName} — {gradeLabel}: {gradeStr}");
            if (!string.IsNullOrWhiteSpace(item.AssignmentName))
            {
                sb.AppendLine($"*{item.AssignmentName}*");
            }
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(displayFeedback))
            {
                sb.AppendLine("**Feedback:**");
                sb.AppendLine($"> {displayFeedback.Replace("\n", "\n> ")}");
            }
            else
            {
                sb.AppendLine("*Feedback ainda não gerado.*");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Edite nota e feedback conforme necessário e confirme o envio ao Moodle.");

        return sb.ToString();
    }

    // ============================================================
    // Helpers
    // ============================================================

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

// ============================================================
// MCP Resource: ui://grading-review/app.html
// ============================================================

[McpServerResourceType]
public sealed class MoodleGradingReviewAppResources
{
    [McpServerResource(
        UriTemplate = MoodleGradingReviewAppMetadata.ResourceUri,
        Name = "grading-review-app",
        Title = "Interface de Revisão de Feedbacks",
        MimeType = MoodleGradingReviewAppMetadata.ResourceMimeType)]
    [Description("Interface HTML interativa para revisar feedbacks de correção assistida.")]
    public IEnumerable<ResourceContents> GetReviewApp()
    {
        // The resource serves the base HTML template.
        // Actual data is delivered through window.openai.toolOutput.
        var html = LoadResourceHtml();

        yield return new TextResourceContents
        {
            Uri = MoodleGradingReviewAppMetadata.ResourceUri,
            MimeType = MoodleGradingReviewAppMetadata.ResourceMimeType,
            Text = html,
            Meta = MoodleGradingReviewAppMetadata.CreateResourceMeta()
        };
    }

    private static string LoadResourceHtml()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("GradingReviewApp.html", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? ".";
        var htmlPath = Path.Combine(assemblyDir, "Tools", "Grading", "GradingReviewApp.html");
        return File.Exists(htmlPath) ? File.ReadAllText(htmlPath, Encoding.UTF8) : "<html><body>Template não encontrado</body></html>";
    }
}

public static class MoodleGradingReviewAppMetadata
{
    public const string ToolName = "revisar_feedbacks_lote";
    public const string ResourceUri = "ui://grading-review/app.html";
    public const string ResourceMimeType = "text/html;profile=mcp-app";

    public static JsonObject CreateToolMeta()
    {
        return new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["resourceUri"] = ResourceUri
            },
            ["openai/outputTemplate"] = ResourceUri
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
            return "https://novascript.com.br";
        }

        configured = configured.Trim().TrimEnd('/');
        if (!configured.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !configured.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            configured = $"https://{configured}";
        }

        return Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : "https://novascript.com.br";
    }
}

// ============================================================
// DTOs
// ============================================================

public sealed record GradingReviewAppData(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("readyItems")] int ReadyItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("progressPercent")] int ProgressPercent,
    [property: JsonPropertyName("items")] IReadOnlyList<GradingReviewItem> Items,
    [property: JsonPropertyName("courseName")] string? CourseName = null);

public sealed record GradingReviewItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("studentName")] string? StudentName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("commitStatus")] string CommitStatus,
    [property: JsonPropertyName("finalGrade")] decimal? FinalGrade,
    [property: JsonPropertyName("finalFeedback")] string? FinalFeedback,
    [property: JsonPropertyName("suggestedGrade")] decimal? SuggestedGrade,
    [property: JsonPropertyName("draftFeedback")] string? DraftFeedback,
    [property: JsonPropertyName("maxGrade")] decimal MaxGrade,
    [property: JsonPropertyName("assignmentName")] string? AssignmentName,
    [property: JsonPropertyName("confidence")] decimal? Confidence);
