using System.Text.Json;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Application.Abstractions;

public sealed record MoodleDraftUploadRequest(
    string Filename,
    string MimeType,
    byte[] Content,
    string FilePath = "/",
    int? ItemId = null);

public sealed record MoodleDraftUploadFile(
    int? ItemId,
    string Filename,
    string FilePath,
    string? Url,
    long? SizeBytes,
    string? MimeType);

public sealed record MoodleDraftUploadResult(
    JsonElement Payload,
    IReadOnlyList<MoodleDraftUploadFile> Files);

/// <summary>
/// Transport for Moodle's external file upload endpoint. The application
/// layer owns authorization and pending actions; this gateway only uploads
/// bytes with the selected user's Moodle token.
/// </summary>
public interface IMoodleDraftUploadGateway
{
    Task<MoodleDraftUploadResult> UploadAsync(
        MoodleConnectorCredentials connection,
        MoodleDraftUploadRequest request,
        CancellationToken cancellationToken);
}
