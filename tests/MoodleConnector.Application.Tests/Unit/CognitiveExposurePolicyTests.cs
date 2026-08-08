using MoodleConnector.Presentation.Configuration;
using Xunit;

namespace MoodleConnector.Application.Tests.Unit;

public class CognitiveExposurePolicyTests
{
    [Fact]
    public void SkillCoursesOptimized_Hides_Course_R1_R2_NonStructural_ButKeeps_Structural()
    {
        var policy = new CognitiveExposurePolicy(ToolExposureProfile.SkillCoursesOptimized);

        var getCourseMeta = new MoodleToolMetadataAttribute
        {
            Family = "courses",
            Classification = "R1",
            Structural = false
        };

        var execReadMeta = new MoodleToolMetadataAttribute
        {
            Family = "universal",
            Classification = "R0",
            Structural = true
        };

        Assert.False(policy.ShouldExpose("get_course", getCourseMeta));
        Assert.True(policy.ShouldExpose("moodle_execute_read", execReadMeta));
    }
}
