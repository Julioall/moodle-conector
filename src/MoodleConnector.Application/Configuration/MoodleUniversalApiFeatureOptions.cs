namespace MoodleConnector.Application.Configuration;

public sealed class MoodleUniversalApiFeatureOptions
{
    public const string SectionName = "Features";

    public bool UniversalMoodleWriteEnabled { get; init; }
}
