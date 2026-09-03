namespace MoodleConnector.Application.Abstractions;

public sealed record MoodleResourceRegistration(
    string ResourceType,
    string Filename,
    string MimeType,
    string RemoteFileReference,
    long? CourseId = null,
    long? AssignmentId = null,
    long? SubmissionId = null,
    long? StudentId = null,
    string? ContextId = null,
    string? Component = null,
    string? FileArea = null,
    string? ItemId = null,
    long? SizeBytes = null,
    string? Sha256 = null);

public sealed record MoodleResourceDescriptor(string Uri, string Filename, string MimeType, long? SizeBytes, string? Sha256);

public sealed record MoodleResourceReadResult(string Uri, string MimeType, byte[] Content, long SizeBytes, string Sha256);

public interface IMoodleResourceGateway
{
    Task<MoodleResourceDescriptor> RegisterAsync(MoodleResourceRegistration request, CancellationToken cancellationToken);
    Task<MoodleResourceReadResult> ReadAsync(string uri, CancellationToken cancellationToken);
    Task<IReadOnlyList<MoodleResourceDescriptor>> ExpandZipAsync(string uri, CancellationToken cancellationToken);
}
