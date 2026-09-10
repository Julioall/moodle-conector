using System.Text.Json;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.MoodleApi;

public sealed record MoodleUploadPreview(
    Guid PendingActionId,
    string ResourceUri,
    string Filename,
    string MimeType,
    long SizeBytes,
    string Sha256,
    string ConnectionAlias,
    string FilePath,
    int? ItemId,
    string ConfirmationText,
    DateTimeOffset ExpiresAt);

public sealed record MoodleUploadResult(
    string Status,
    Guid PendingActionId,
    IReadOnlyList<MoodleDraftUploadFile> Files,
    string? AuditId,
    IReadOnlyList<string>? Warnings = null,
    JsonElement? Payload = null);

public interface IMoodleUniversalUploadService
{
    Task<MoodleUploadPreview> PrepareAsync(
        string resourceUri,
        string? filename,
        string? mimeType,
        string filePath,
        int? itemId,
        CancellationToken cancellationToken);

    Task<MoodleUploadResult> ConfirmAsync(
        Guid pendingActionId,
        string confirmationText,
        CancellationToken cancellationToken);
}
