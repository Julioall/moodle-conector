using Xunit;
using MoodleConnector.Presentation.Configuration;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Application.Tests.Unit;

public class ToolMetadataRegistryTests
{
    [Fact]
    public void RegisterFromType_PopulatesRegistryForCourseTools()
    {
        var reg = new ToolMetadataRegistry();
        reg.RegisterFromType(typeof(MoodleCoursesTools));

        Assert.True(reg.TryGet("get_course", out var meta));
        Assert.NotNull(meta);
        Assert.Equal("courses", meta!.Family);
        Assert.Equal("R1", meta.Classification);
        Assert.False(meta.Structural);
    }
}
