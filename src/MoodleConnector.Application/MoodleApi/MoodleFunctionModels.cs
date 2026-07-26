using System.Text.Json;

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
    bool IsAvailable);

public sealed record MoodleFunctionProfile(
    string ConnectionId,
    string ConnectionAlias,
    string? SiteName,
    string? Release,
    long? MoodleUserId,
    IReadOnlyList<MoodleFunctionDescriptor> Functions,
    DateTimeOffset DiscoveredAt);

public sealed record MoodleFunctionResult(
    string Function,
    JsonElement Payload);

public sealed class MoodleApiException(
    string errorCode,
    string message,
    int? httpStatusCode = null) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int? HttpStatusCode { get; } = httpStatusCode;
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
    DateTimeOffset ExpiresAt);

public sealed record MoodleWriteResult(
    string Status,
    Guid PendingActionId,
    string Function,
    string? AuditId,
    int ResponseSize);

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
