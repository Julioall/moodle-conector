using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Tools.Grading;
using MoodleConnector.Presentation.Tools.Gradebook;
using MoodleConnector.Presentation.Tools.Memory;
using MoodleConnector.Presentation.Tools.Messages;
using MoodleConnector.Presentation.Tools.Pedagogy;
using MoodleConnector.Presentation.Tools.Portal;
using MoodleConnector.Presentation.Tools.Risk;
using MoodleConnector.Presentation.Tools.Reports;

namespace MoodleConnector.Application.Tests.Tools;

public sealed class McpToolMetadataTests
{
    [Fact]
    public void ToolsMoodleDevemUsarHintsConservadoresParaAppsSdk()
    {
        foreach (var (toolType, method, attribute) in EnumerateToolAttributes())
        {
            var toolName = attribute.Name ?? method.Name;

            Assert.False(attribute.OpenWorld, $"{toolName} deve declarar OpenWorld=false.");
            if (toolName is "confirm_batch_grade_launch" or "confirm_forum_post" or "confirm_individual_grade_launch" or
                "confirm_welcome_message" or "confirm_access_reminder" or "confirm_activity_reminder" or
                "confirm_recovery_message" or "confirm_closing_message" or "confirm_followup_message" or
                "manage_user_memory" or "save_user_memory_document" or "remove_user_memory_document" or
                "cancel_assisted_grading_batch" or "update_grading_draft" or "update_grading_drafts_batch" or
                "moodle_confirm_write")
            {
                Assert.True(attribute.Destructive, $"{toolName} deve declarar Destructive=true.");
            }
            else
            {
                Assert.False(attribute.Destructive, $"{toolName} deve declarar Destructive=false.");
            }

            if (toolName is "manage_user_memory" or "save_user_memory_document")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} grava estado interno e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser idempotente.");
            }
            else if (toolName == "remove_user_memory_document")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} remove estado interno e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser retry-safe pelo documentId.");
            }
            else if (toolName is "create_assisted_grading_batch" or "start_pending_grading_run")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} cria job interno e deve declarar ReadOnly=false.");
                Assert.False(attribute.Idempotent, $"{toolName} nao deve ser idempotente sem chave de idempotencia.");
            }
            else if (toolName is "create_batch_grade_launch_preview" or "create_forum_post_preview")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} cria acao pendente e deve declarar ReadOnly=false.");
                Assert.False(attribute.Idempotent, $"{toolName} nao deve ser idempotente sem chave de idempotencia.");
            }
            else if (toolName is "confirm_batch_grade_launch" or "confirm_forum_post")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} executa escrita Moodle e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser retry-safe pelo pendingActionId.");
            }
            else if (toolName is "update_grading_draft" or "update_grading_drafts_batch" or "save_ai_grading_batch")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} atualiza estado interno e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser idempotente com payload identico.");
            }
            else if (toolName == "cancel_assisted_grading_batch")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} cancela job interno e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser retry-safe por batchJobId.");
            }
            else if (toolName is "prepare_welcome_message" or "prepare_access_reminder" or "prepare_activity_reminder" or
                     "prepare_recovery_message" or "prepare_closing_message" or "prepare_followup_message")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} cria uma acao pendente e deve declarar ReadOnly=false.");
                Assert.False(attribute.Idempotent, $"{toolName} cria uma nova acao pendente a cada chamada.");
            }
            else if (toolName is "confirm_welcome_message" or "confirm_access_reminder" or "confirm_activity_reminder" or
                     "confirm_recovery_message" or "confirm_closing_message" or "confirm_followup_message" or
                     "moodle_prepare_write" or "moodle_confirm_write" or "moodle_reconcile_write")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} altera estado e deve declarar ReadOnly=false.");
                if (toolName != "moodle_reconcile_write")
                {
                    Assert.False(attribute.Idempotent, $"{toolName} nao declara semantica retry-safe.");
                }
            }
            else
            {
                Assert.True(attribute.Idempotent, $"{toolName} deve declarar Idempotent=true.");
                Assert.True(attribute.ReadOnly, $"{toolName} deve declarar ReadOnly=true enquanto for tool de leitura Moodle.");
            }
        }
    }

    [Fact]
    public void ReviewAppDeveExporResourceMetadataPadraoAppsSdk()
    {
        var method = typeof(MoodleGradingReviewAppResources)
            .GetMethod(nameof(MoodleGradingReviewAppResources.GetReviewApp))!;
        var attribute = method.GetCustomAttribute<McpServerResourceAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceUri, attribute!.UriTemplate);
        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceMimeType, attribute.MimeType);
        Assert.Contains("/v2/", MoodleGradingReviewAppMetadata.ResourceUri);

        var resource = Assert.Single(new MoodleGradingReviewAppResources().GetReviewApp());
        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceUri, resource.Uri);
        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceMimeType, resource.MimeType);

        var meta = resource.Meta;
        Assert.NotNull(meta);
        var ui = Assert.IsType<JsonObject>(meta!["ui"]);
        Assert.Equal(true, ui["prefersBorder"]?.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(ui["domain"]?.GetValue<string>()));

        var csp = Assert.IsType<JsonObject>(ui["csp"]);
        Assert.Empty(Assert.IsType<JsonArray>(csp["connectDomains"]));
        Assert.Empty(Assert.IsType<JsonArray>(csp["resourceDomains"]));

        Assert.False(string.IsNullOrWhiteSpace(meta["openai/widgetDescription"]?.GetValue<string>()));
        Assert.Equal(true, meta["openai/widgetPrefersBorder"]?.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(meta["openai/widgetDomain"]?.GetValue<string>()));
        Assert.IsType<JsonObject>(meta["openai/widgetCSP"]);
    }

    [Fact]
    public void ReviewAppToolDeveExporResourceUriPadraoAppsSdk()
    {
        var meta = MoodleGradingReviewAppMetadata.CreateToolMeta();
        var ui = Assert.IsType<JsonObject>(meta["ui"]);

        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceUri, ui["resourceUri"]?.GetValue<string>());
        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceUri, meta["openai/outputTemplate"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(meta["openai/toolInvocation/invoking"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(meta["openai/toolInvocation/invoked"]?.GetValue<string>()));
    }

    [Fact]
    public void ReviewAppDeveUsarBridgePadraoEPreservarEstadoVisual()
    {
        var resource = Assert.IsType<TextResourceContents>(
            Assert.Single(new MoodleGradingReviewAppResources().GetReviewApp()));
        var html = resource.Text;

        Assert.Contains("ui/notifications/tool-result", html);
        Assert.Contains("tools/call", html);
        Assert.Contains("window.openai?.widgetState", html);
        Assert.Contains("setWidgetState", html);
        Assert.Contains("privateContent", html);
        Assert.Contains("get_batch_grading_ui_state", html);
        Assert.Contains("update_grading_drafts_batch", html);
        Assert.Contains("create_batch_grade_launch_preview", html);
        Assert.Contains("confirm_batch_grade_launch", html);
        Assert.DoesNotContain("atualizar_rascunhos_correcao_lote", html);
        Assert.DoesNotContain("criar_previa_lancamento_lote", html);
        Assert.DoesNotContain("confirmar_lancamento_lote_moodle", html);
        Assert.Contains("hydration-error", html);
        Assert.Contains("O lote não foi marcado como vazio", html);
        Assert.Contains("empty-state", html);
        Assert.DoesNotContain("fonts.googleapis.com", html);
        Assert.DoesNotContain("localStorage", html);
    }

    [Fact]
    public void ReviewAppDtoDeveExporEstadoAutoritativoEPaginacao()
    {
        Assert.NotNull(typeof(GradingReviewAppData).GetProperty(nameof(GradingReviewAppData.Page)));
        Assert.NotNull(typeof(GradingReviewAppData).GetProperty(nameof(GradingReviewAppData.PageSize)));
        Assert.NotNull(typeof(GradingReviewAppData).GetProperty(nameof(GradingReviewAppData.HasMore)));

        Assert.NotNull(typeof(GradingReviewItem).GetProperty(nameof(GradingReviewItem.WorkflowState)));
        Assert.NotNull(typeof(GradingReviewItem).GetProperty(nameof(GradingReviewItem.CanEdit)));
        Assert.NotNull(typeof(GradingReviewItem).GetProperty(nameof(GradingReviewItem.CanSelect)));
        Assert.NotNull(typeof(GradingReviewItem).GetProperty(nameof(GradingReviewItem.CanSend)));
        Assert.NotNull(typeof(GradingReviewItem).GetProperty(nameof(GradingReviewItem.StatusReason)));
        Assert.NotNull(typeof(GradingReviewItem).GetProperty(nameof(GradingReviewItem.DraftVersionHash)));
    }

    [Fact]
    public void SnapshotDaInterfaceDeveSerToolDeDadosSemMutacao()
    {
        var method = typeof(MoodleGradingReviewAppTools)
            .GetMethod(nameof(MoodleGradingReviewAppTools.ConsultarEstadoInterfaceCorrecaoLoteAsync))!;
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("get_batch_grading_ui_state", attribute!.Name);
        Assert.True(attribute.ReadOnly);
        Assert.True(attribute.Idempotent);
        Assert.False(attribute.Destructive);
        Assert.False(attribute.OpenWorld);
    }

    [Fact]
    public void AtualizacoesQuePodemSobrescreverEstadoDevemSerMarcadasComoDestrutivas()
    {
        Assert.True(GetToolAttribute(typeof(PortalTaskTools), "update_task").Destructive);
        Assert.True(GetToolAttribute(typeof(PortalAgendaTools), "update_agenda_event").Destructive);
    }

    private static IEnumerable<(Type ToolType, MethodInfo Method, McpServerToolAttribute Attribute)> EnumerateToolAttributes()
    {
        var toolTypes = new[]
        {
            typeof(MoodleCoursesTools),
            typeof(MoodleParticipantsTools),
            typeof(MoodleCourseContentsTools),
            typeof(MoodleCourseActivitiesTools),
            typeof(MoodleForumTools),
            typeof(MoodleAssignmentSubmissionsTools),
            typeof(MoodleGradingTools),
            typeof(MoodleGradebookTools),
            typeof(MoodleRiskAnalysisTools),
            typeof(MoodleReportTools),
            typeof(MoodleGradingContextDiagnosticsTools),
            typeof(MoodleGradingReviewAppTools),
            typeof(MoodleMemoryTools),
            typeof(MoodleMemoryDocumentTools),
            typeof(MoodlePedagogyTools),
            typeof(MoodleTutorMessageTools),
            typeof(MoodleUniversalWriteTools)
        };

        foreach (var toolType in toolTypes)
        {
            foreach (var method in toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute is not null)
                {
                    yield return (toolType, method, attribute);
                }
            }
        }
    }

    private static McpServerToolAttribute GetToolAttribute(Type toolType, string toolName) =>
        toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Single(attribute => attribute?.Name == toolName)!;
}
