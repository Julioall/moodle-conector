using System.Reflection;
using ModelContextProtocol.Server;
using MoodleConnector.Presentation.Tools;

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
            Assert.False(attribute.Destructive, $"{toolName} deve declarar Destructive=false.");
            Assert.True(attribute.Idempotent, $"{toolName} deve declarar Idempotent=true.");

            if (toolType != typeof(DemoPendingActionTools))
            {
                Assert.True(attribute.ReadOnly, $"{toolName} deve declarar ReadOnly=true enquanto for tool de leitura Moodle.");
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
