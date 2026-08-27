namespace MoodleConnector.Application.Configuration;

public sealed class GradingLimitsOptions
{
    public const string SectionName = "GradingLimits";

    public int MaxBatchItems { get; init; } = 400;

    public int MaxFileSizeMb { get; init; } = 25;

    public int MaxFilesPerSubmission { get; init; } = 10;

    public int MaxTextCharsPerSubmission { get; init; } = 120_000;

    public int MaxReviewItemsPerPage { get; init; } = 25;

    public int RawFileRetentionDays { get; init; } = 7;

    public int DraftRetentionDays { get; init; } = 180;

    public int MoodleMaxConcurrentRequests { get; init; } = 5;

    public int FileDownloadWorkers { get; init; } = 5;

    public int ExtractionWorkers { get; init; } = 4;

    public int AiAnalysisWorkers { get; init; } = 3;

    public int BatchLeaseMinutes { get; init; } = 15;

    public int DurableBatchPollSeconds { get; init; } = 5;

    public int DurableBatchClaimSize { get; init; } = 4;
}
