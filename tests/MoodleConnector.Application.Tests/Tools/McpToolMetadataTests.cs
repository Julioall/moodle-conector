using System.Reflection;
using ModelContextProtocol.Server;
using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Tools.Grading;

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

    private static IEnumerable<(Type ToolType, MethodInfo Method, McpServerToolAttribute Attribute)> EnumerateToolAttributes()
    {
        var toolTypes = new[]
        {
            typeof(MoodleCoursesTools),
            typeof(MoodleParticipantsTools),
            typeof(MoodleCourseContentsTools),
            typeof(MoodleCourseActivitiesTools),
            typeof(MoodleAssignmentSubmissionsTools),
            typeof(MoodleGradingTools),
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
