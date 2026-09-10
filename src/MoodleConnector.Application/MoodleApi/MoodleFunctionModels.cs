using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoodleConnector.Application.MoodleApi;

public enum MoodleFunctionRisk
{
    Unknown,
    Read,
    ControlledWrite,
    Destructive
}

public sealed record MoodleFunctionDescriptor(
    string Name,
    MoodleFunctionRisk Risk,
    bool IsAvailable,
    MoodleContractStatus ContractStatus = MoodleContractStatus.Missing,
    string? ContractHash = null,
    IReadOnlyList<string>? ContractReasons = null,
    string? ExternalFunctionVersion = null);

public sealed record MoodleFunctionProfile(
    string ConnectionId,
    string ConnectionAlias,
    string? SiteName,
    string? Release,
    long? MoodleUserId,
    IReadOnlyList<MoodleFunctionDescriptor> Functions,
    DateTimeOffset DiscoveredAt,
    bool IsCached = false);

public sealed record MoodleFunctionResult(
    string Function,
    JsonElement Payload,
    string? OperationId = null,
    string? ConnectionAlias = null,
    string? ContractHash = null,
    string Status = "executed",
    MoodleResultCompleteness? Completeness = null);

public sealed record MoodleResultCompleteness(
    int? ReturnedCount,
    bool Truncated,
    bool? HasMore,
    string? ContinuationToken,
    string? Reason);

public sealed record MoodleFunctionDescription(
    string FunctionName,
    bool IsAvailable,
    MoodleFunctionRisk HeuristicRisk,
    string? ExternalFunctionVersion,
    MoodleContractStatus ContractStatus,
    string? ContractHash,
    IReadOnlyList<string> ContractReasons,
    MoodleEffect? ContractEffect,
    string? MoodleVersion,
    string? Component,
    string? PluginVersion,
    string? Source,
    JsonElement? InputSchema,
    JsonElement? OutputSchema,
    MoodlePaginationContract? Pagination = null,
    MoodleFileHandlingContract? FileHandling = null,
    IReadOnlyList<string>? RequiredScopes = null,
    string? PlatformPermission = null,
    bool AdministrativeOnly = false,
    IReadOnlyList<string>? MoodleCapabilities = null);

public sealed record MoodleFunctionCoverageItem(
    string FunctionName,
    bool IsAvailable,
    MoodleFunctionRisk HeuristicRisk,
    string? ExternalFunctionVersion,
    MoodleContractStatus ContractStatus,
    MoodleEffect? ContractEffect,
    string CoverageState,
    bool ExecutionReady,
    string? ContractHash,
    IReadOnlyList<string> Reasons,
    string TransportStatus = "rest_supported",
    string HomologationStatus = "not_homologated",
    bool AdministrativeOnly = false);

public sealed record MoodleFunctionCoverageReport(
    string ConnectionId,
    string ConnectionAlias,
    string? MoodleRelease,
    DateTimeOffset DiscoveredAt,
    bool DiscoveryComplete,
    bool IsCached,
    int TotalDiscovered,
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyDictionary<string, int> StateCounts,
    IReadOnlyList<MoodleFunctionCoverageItem> Items,
    IReadOnlyList<string> Warnings);

public sealed record MoodleOperationResult(
    [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
    [property: JsonPropertyName("toolName")] string ToolName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("function")] string? Function,
    [property: JsonPropertyName("contractHash")] string? ContractHash,
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("files")] JsonElement? Files,
    [property: JsonPropertyName("resultUpdatedAtUtc")] DateTimeOffset? ResultUpdatedAtUtc,
    [property: JsonPropertyName("executionUnknown")] bool ExecutionUnknown,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public enum MoodleIntegrationStage
{
    Unknown = 0,
    ConnectionLookup = 10,
    ConnectionState = 20,
    UrlValidation = 30,
    CredentialPresence = 40,
    CredentialDecryption = 50,
    TokenRequest = 60,
    MoodleRequest = 70,
    ResponseParsing = 80
}

public sealed class MoodleApiException : Exception
{
    public MoodleApiException(
        string errorCode,
        string message,
        int? httpStatusCode = null,
        Exception? innerException = null,
        string? auditId = null,
        string? connectionId = null,
        string? connectionAlias = null,
        string? endpoint = null,
        string? functionName = null,
        long? durationMs = null,
        string? remoteErrorCode = null,
        MoodleIntegrationStage stage = MoodleIntegrationStage.Unknown)
        : base(message, innerException)
    {
        ErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? MoodleErrorContract.Unexpected
            : errorCode.Trim().ToLowerInvariant();
        HttpStatusCode = httpStatusCode;
        AuditId = string.IsNullOrWhiteSpace(auditId) ? Guid.NewGuid().ToString("N") : auditId;
        ConnectionId = connectionId;
        ConnectionAlias = connectionAlias;
        Endpoint = endpoint;
        FunctionName = functionName;
        DurationMs = durationMs;
        RemoteErrorCode = remoteErrorCode;
        Stage = stage;
    }

    public string ErrorCode { get; }
    public int? HttpStatusCode { get; }
    public string AuditId { get; }
    public string? ConnectionId { get; }
    public string? ConnectionAlias { get; }
    public string? Endpoint { get; }
    public string? FunctionName { get; }
    public long? DurationMs { get; }
    public string? RemoteErrorCode { get; }
    public MoodleIntegrationStage Stage { get; }
}

public interface IMoodleFunctionCatalog
{
    Task<MoodleFunctionProfile> GetCurrentAsync(bool forceRefresh, CancellationToken cancellationToken);
}

public interface IMoodleFunctionExecutor
{
    Task<MoodleFunctionResult> ExecuteReadAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);
}

public sealed record MoodleWritePreview(
    Guid PendingActionId,
    string Function,
    IReadOnlyList<string> ParameterNames,
    string ParameterHash,
    string ConfirmationText,
    DateTimeOffset ExpiresAt,
    string? SemanticSummary = null,
    IReadOnlyList<MoodleWritePreviewChange>? Changes = null,
    IReadOnlyList<string>? AffectedResources = null,
    int? EstimatedAffectedRecords = null,
    IReadOnlyList<string>? Warnings = null,
    string? ContractHash = null,
    MoodleContractStatus ContractStatus = MoodleContractStatus.Missing);

public sealed record MoodleWritePreviewChange(
    string Name,
    string? PreviousValue,
    string? NewValue);

public sealed record MoodleWriteResult(
    string Status,
    Guid PendingActionId,
    string Function,
    string? AuditId,
    int ResponseSize,
    JsonElement? Payload = null,
    string? ContractHash = null,
    IReadOnlyList<string>? Warnings = null);

public interface IMoodleUniversalWriteService
{
    Task<MoodleWritePreview> PrepareAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);

    Task<MoodleWriteResult> ConfirmAsync(
        Guid pendingActionId,
        string confirmationText,
        CancellationToken cancellationToken);
}
