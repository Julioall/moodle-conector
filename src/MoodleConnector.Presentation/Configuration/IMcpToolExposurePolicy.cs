namespace MoodleConnector.Presentation.Configuration;

public interface IMcpToolExposurePolicy
{
    bool ShouldExpose(string toolName, MoodleToolMetadataAttribute? metadata);
}
