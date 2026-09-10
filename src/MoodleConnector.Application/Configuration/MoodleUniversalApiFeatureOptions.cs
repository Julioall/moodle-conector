namespace MoodleConnector.Application.Configuration;

public sealed class MoodleUniversalApiFeatureOptions
{
    public const string SectionName = "Features";

    public bool UniversalMoodleWriteEnabled { get; init; }

    public bool UniversalMoodleFileDownloadEnabled { get; init; }

    public bool UniversalMoodleFileUploadEnabled { get; init; }

    /// <summary>
    /// MIME policy for binary delivery. Extraction support is not required for
    /// a file to be delivered as an MCP resource. An empty list is fail-closed.
    /// </summary>
    public string[] UniversalMoodleAllowedFileMimeTypes { get; init; } =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "text/plain",
        "text/rtf",
        "text/csv",
        "application/json",
        "application/xml",
        "application/zip",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.ms-powerpoint",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/svg+xml",
        "application/octet-stream"
    ];

    // Delivery of submission files through opaque MCP resources. This is the
    // only delivery path used by assisted grading.
    public bool McpResourceSubmissionDeliveryEnabled { get; init; }

    // Retained only so existing configuration files continue to bind. No
    // correction code reads this flag or invokes a legacy extractor.
    public bool LegacySubmissionExtractionEnabled { get; init; }

    public bool McpResourceZipEnabled { get; init; }

    public bool McpGradingDraftEnabled { get; init; }

    public bool McpGradingWriteEnabled { get; init; }

    /// <summary>
    /// Reclassifica falhas técnicas de validação como avisos auditáveis durante
    /// a correção assistida. Salvaguardas de identidade, autorização,
    /// duplicidade, sobrescrita, escala e tentativa continuam impeditivas.
    /// </summary>
    public bool McpGradingSecurityWarningsOnly { get; init; }
}
