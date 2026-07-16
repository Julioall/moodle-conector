namespace MoodleConnector.Application.Configuration;

public sealed class MessageWriteFeatureOptions
{
    public const string SectionName = "Features";

    public bool MessagesWriteEnabled { get; init; }
}
