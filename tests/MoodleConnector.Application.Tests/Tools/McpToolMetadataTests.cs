using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Tools.Completion;
using MoodleConnector.Presentation.Tools.Grading;
using MoodleConnector.Presentation.Tools.Gradebook;
using MoodleConnector.Presentation.Tools.Risk;

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
            if (toolName == "confirmar_lancamento_lote_moodle")
            {
                Assert.True(attribute.Destructive, $"{toolName} deve declarar Destructive=true.");
            }
            else
            {
                Assert.False(attribute.Destructive, $"{toolName} deve declarar Destructive=false.");
            }
            if (toolName == "criar_lote_correcao_assistida")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} cria job interno e deve declarar ReadOnly=false.");
                Assert.False(attribute.Idempotent, $"{toolName} nao deve ser idempotente sem chave de idempotencia.");
            }
            else if (toolName == "criar_previa_lancamento_lote")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} cria acao pendente e deve declarar ReadOnly=false.");
                Assert.False(attribute.Idempotent, $"{toolName} nao deve ser idempotente sem chave de idempotencia.");
            }
            else if (toolName == "confirmar_lancamento_lote_moodle")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} executa escrita Moodle e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser retry-safe pelo pendingActionId.");
            }
            else if (toolName == "atualizar_rascunho_correcao")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} atualiza estado interno e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser idempotente com expectedReviewStatus e payload identico.");
            }
            else if (toolName == "cancelar_lote_correcao_assistida")
            {
                Assert.False(attribute.ReadOnly, $"{toolName} cancela job interno e deve declarar ReadOnly=false.");
                Assert.True(attribute.Idempotent, $"{toolName} deve ser retry-safe por batchJobId.");
            }
            else
            {
                Assert.True(attribute.Idempotent, $"{toolName} deve declarar Idempotent=true.");

                if (toolType != typeof(DemoPendingActionTools))
                {
                    Assert.True(attribute.ReadOnly, $"{toolName} deve declarar ReadOnly=true enquanto for tool de leitura Moodle.");
                }
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

        var resource = Assert.Single(new MoodleGradingReviewAppResources().GetReviewApp());
        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceUri, resource.Uri);
        Assert.Equal(MoodleGradingReviewAppMetadata.ResourceMimeType, resource.MimeType);

        var meta = resource.Meta;
        Assert.NotNull(meta);

        var ui = Assert.IsType<JsonObject>(meta!["ui"]);
        Assert.Equal(true, ui["prefersBorder"]?.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(ui["domain"]?.GetValue<string>()));

        var csp = Assert.IsType<JsonObject>(ui["csp"]);
        Assert.IsType<JsonArray>(csp["connectDomains"]);
        Assert.IsType<JsonArray>(csp["resourceDomains"]);

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
            typeof(MoodleCompletionTools),
            typeof(MoodleRiskAnalysisTools),
            typeof(MoodleGradingContextDiagnosticsTools),
            typeof(MoodleGradingReviewAppTools),
            typeof(DemoPendingActionTools)
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
}
