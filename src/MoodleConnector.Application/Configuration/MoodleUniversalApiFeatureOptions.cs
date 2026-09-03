namespace MoodleConnector.Application.Configuration;

public sealed class MoodleUniversalApiFeatureOptions
{
    public const string SectionName = "Features";

    public bool UniversalMoodleWriteEnabled { get; init; }

    public bool UniversalMoodleFileDownloadEnabled { get; init; }

    // Delivery of submission files through opaque MCP resources. Kept off until
    // the resource gateway is enabled for an explicitly approved rollout.
    public bool McpResourceSubmissionDeliveryEnabled { get; init; }

    public bool LegacySubmissionExtractionEnabled { get; init; }

    public bool McpResourceZipEnabled { get; init; }

    public bool McpGradingDraftEnabled { get; init; }

    public bool McpGradingWriteEnabled { get; init; }
}
