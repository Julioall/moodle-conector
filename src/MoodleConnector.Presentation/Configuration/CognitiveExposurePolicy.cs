namespace MoodleConnector.Presentation.Configuration;

public enum ToolExposureProfile
{
    Full,
    FullWithCoursesSkill,
    SkillCoursesOptimized
}

public sealed class CognitiveExposurePolicy : IMcpToolExposurePolicy
{
    private readonly ToolExposureProfile _profile;

    public CognitiveExposurePolicy(ToolExposureProfile profile)
    {
        _profile = profile;
    }

    public bool ShouldExpose(string toolName, MoodleToolMetadataAttribute? metadata)
    {
        if (metadata == null) return true;

        if (_profile == ToolExposureProfile.SkillCoursesOptimized)
        {
            // Profile C: hide only non-structural Course wrappers classified as R1/R2.
            // Do NOT hide structural primitives like `moodle_execute_read` or
            // registry/discovery helpers at this stage.
            if (metadata.Family == "courses" &&
                (metadata.Classification == "R1" || metadata.Classification == "R2") &&
                !metadata.Structural)
            {
                return false;
            }
        }

        return true;
    }
}
