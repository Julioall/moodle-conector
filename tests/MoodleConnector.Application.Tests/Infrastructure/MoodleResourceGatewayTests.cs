using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleResourceGatewayTests
{
    [Fact]
    public async Task ReadAsync_RevalidatesConnectionAndReturnsOriginalBytes()
    {
        await using var db = CreateDb();
        var files = new FileGateway();
        var credentials = new Credentials("client-a", "connection-a");
        var gateway = CreateGateway(db, files, credentials);
        var descriptor = await gateway.RegisterAsync(new MoodleResourceRegistration("submission_attachment", "atividade.pdf", "application/pdf", "https://moodle.example/pluginfile.php/1/a.pdf?token=never-persist"), CancellationToken.None);

        var result = await gateway.ReadAsync(descriptor.Uri, CancellationToken.None);

        Assert.Equal(files.Bytes, result.Content);
        Assert.Equal("application/pdf", result.MimeType);
        Assert.DoesNotContain("token", (await db.MoodleResources.SingleAsync()).RemoteFileReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RejectsResourceFromAnotherConnection()
    {
        await using var db = CreateDb();
        var registered = CreateGateway(db, new FileGateway(), new Credentials("client-a", "connection-a"));
        var descriptor = await registered.RegisterAsync(new MoodleResourceRegistration("submission_attachment", "a.txt", "text/plain", "https://moodle.example/pluginfile.php/1/a.txt"), CancellationToken.None);
        var reader = CreateGateway(db, new FileGateway(), new Credentials("client-b", "connection-b"));

        var exception = await Assert.ThrowsAsync<MoodleResourceException>(() => reader.ReadAsync(descriptor.Uri, CancellationToken.None));
        Assert.Equal("RESOURCE_FORBIDDEN", exception.ErrorCode);
    }

    [Fact]
    public async Task ReadAsync_RejectsAnotherUserOnTheSameConnectorConnection()
    {
        await using var db = CreateDb();
        var credentials = new Credentials("client-a", "connection-a");
        var registered = CreateGateway(db, new FileGateway(), credentials, user: new User("teacher-a"));
        var descriptor = await registered.RegisterAsync(new MoodleResourceRegistration("submission_attachment", "a.txt", "text/plain", "https://moodle.example/pluginfile.php/1/a.txt"), CancellationToken.None);
        var reader = CreateGateway(db, new FileGateway(), credentials, user: new User("teacher-b"));

        var exception = await Assert.ThrowsAsync<MoodleResourceException>(() => reader.ReadAsync(descriptor.Uri, CancellationToken.None));

        Assert.Equal("RESOURCE_FORBIDDEN", exception.ErrorCode);
    }

    [Fact]
    public async Task ReadAsync_DeliversBytesWithoutInspectingMimeOrSignature()
    {
        await using var db = CreateDb();
        var gateway = CreateGateway(db, new InvalidFileGateway(), new Credentials("client-a", "connection-a"));
        var descriptor = await gateway.RegisterAsync(new MoodleResourceRegistration("submission_attachment", "atividade.pdf", "application/pdf", "https://moodle.example/pluginfile.php/1/a.pdf"), CancellationToken.None);

        var result = await gateway.ReadAsync(descriptor.Uri, CancellationToken.None);

        Assert.Equal([1, 2, 3], result.Content);
        Assert.Equal("image/png", result.MimeType);
    }

    [Theory]
    [InlineData("resposta.rtf", "text/rtf")]
    [InlineData("resposta.rtf", "application/octet-stream")]
    [InlineData("resposta.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("resposta.pdf", "application/pdf")]
    [InlineData("planilha.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task ReadAsync_PreservesOriginalFileForCommonSubmissionFormats(string filename, string mimeType)
    {
        await using var db = CreateDb();
        var original = System.Text.Encoding.UTF8.GetBytes($"bytes originais de {filename}");
        var gateway = CreateGateway(
            db,
            new TypedFileGateway(mimeType, original),
            new Credentials("client-a", "connection-a"));
        var descriptor = await gateway.RegisterAsync(
            new MoodleResourceRegistration(
                "submission_attachment",
                filename,
                mimeType,
                $"https://moodle.example/pluginfile.php/1/{filename}"),
            CancellationToken.None);

        var result = await gateway.ReadAsync(descriptor.Uri, CancellationToken.None);

        Assert.Equal(original, result.Content);
        Assert.Equal(mimeType, result.MimeType);
        Assert.Equal(filename, descriptor.Filename);
        Assert.Equal(mimeType, descriptor.MimeType);
    }

    [Fact]
    public async Task ReadAsync_WhenMoodleDownloadFails_DoesNotReturnContent()
    {
        await using var db = CreateDb();
        var gateway = CreateGateway(
            db,
            new FailingFileGateway(),
            new Credentials("client-a", "connection-a"));
        var descriptor = await gateway.RegisterAsync(
            new MoodleResourceRegistration(
                "submission_attachment",
                "resposta.rtf",
                "text/rtf",
                "https://moodle.example/pluginfile.php/1/resposta.rtf"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<MoodleResourceException>(
            () => gateway.ReadAsync(descriptor.Uri, CancellationToken.None));

        Assert.Equal("RESOURCE_DOWNLOAD_FAILED", exception.ErrorCode);
    }

    [Fact]
    public async Task ExpandZipAsync_CreatesOpaqueChildResources()
    {
        await using var db = CreateDb();
        var gateway = CreateGateway(db, new ZipFileGateway(CreateZip("resposta.txt", "conteudo"u8.ToArray())), new Credentials("client-a", "connection-a"), zipEnabled: true);
        var parent = await gateway.RegisterAsync(new MoodleResourceRegistration("submission_attachment", "entrega.zip", "application/zip", "https://moodle.example/pluginfile.php/1/a.zip"), CancellationToken.None);

        var children = await gateway.ExpandZipAsync(parent.Uri, CancellationToken.None);

        var child = Assert.Single(children);
        Assert.Equal("resposta.txt", child.Filename);
        Assert.StartsWith("moodle://resource/", child.Uri, StringComparison.Ordinal);
        Assert.Equal("conteudo", System.Text.Encoding.UTF8.GetString((await gateway.ReadAsync(child.Uri, CancellationToken.None)).Content));
    }

    [Fact]
    public async Task ExpandZipAsync_BloqueiaPathTraversal()
    {
        await using var db = CreateDb();
        var gateway = CreateGateway(db, new ZipFileGateway(CreateZip("../escape.txt", "conteudo"u8.ToArray())), new Credentials("client-a", "connection-a"), zipEnabled: true);
        var parent = await gateway.RegisterAsync(new MoodleResourceRegistration("submission_attachment", "entrega.zip", "application/zip", "https://moodle.example/pluginfile.php/1/a.zip"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<MoodleResourceException>(() => gateway.ExpandZipAsync(parent.Uri, CancellationToken.None));

        Assert.Equal("RESOURCE_UNSUPPORTED", exception.ErrorCode);
    }

    private static ConnectorDbContext CreateDb() => new(new DbContextOptionsBuilder<ConnectorDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
    private static MoodleResourceGateway CreateGateway(ConnectorDbContext db, IMoodleSubmissionFileGateway files, IMoodleConnectorCredentialsProvider credentials, bool zipEnabled = false, ICurrentUserContext? user = null) => new(new MoodleResourceRepository(db), files, credentials, user ?? new User(), Options.Create(new GradingLimitsOptions()), Options.Create(new MoodleUniversalApiFeatureOptions { McpResourceSubmissionDeliveryEnabled = true, McpResourceZipEnabled = zipEnabled }), new Audits(), new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
    private static byte[] CreateZip(string name, byte[] content) { using var stream = new MemoryStream(); using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) { var entry = archive.CreateEntry(name); using var output = entry.Open(); output.Write(content); } return stream.ToArray(); }

    private sealed class FileGateway : IMoodleSubmissionFileGateway
    {
        public byte[] Bytes { get; } = "%PDF-1.7"u8.ToArray();
        public Task<SubmissionFileDownloadResult> DownloadFileAsync(string userExternalId, string fileUrl, string filename, long maxBytes, CancellationToken cancellationToken) => Task.FromResult(new SubmissionFileDownloadResult(filename, filename.EndsWith(".pdf", StringComparison.Ordinal) ? "application/pdf" : "text/plain", Bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Bytes)).ToLowerInvariant(), Bytes, false));
    }
    private sealed class Credentials(string clientId, string connectionId) : IMoodleConnectorCredentialsProvider { public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) => Task.FromResult(new MoodleConnectorCredentials(clientId, connectionId, "default", "https://moodle.example", "u", "p", "default", false)); }
    private sealed class InvalidFileGateway : IMoodleSubmissionFileGateway { public Task<SubmissionFileDownloadResult> DownloadFileAsync(string userExternalId, string fileUrl, string filename, long maxBytes, CancellationToken cancellationToken) => Task.FromResult(new SubmissionFileDownloadResult(filename, "image/png", 3, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", [1, 2, 3], false)); }
    private sealed class TypedFileGateway(string mimeType, byte[] bytes) : IMoodleSubmissionFileGateway
    {
        public Task<SubmissionFileDownloadResult> DownloadFileAsync(string userExternalId, string fileUrl, string filename, long maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(new SubmissionFileDownloadResult(
                filename,
                mimeType,
                bytes.LongLength,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes,
                false));
    }
    private sealed class FailingFileGateway : IMoodleSubmissionFileGateway
    {
        public Task<SubmissionFileDownloadResult> DownloadFileAsync(string userExternalId, string fileUrl, string filename, long maxBytes, CancellationToken cancellationToken) =>
            Task.FromException<SubmissionFileDownloadResult>(new InvalidOperationException("download failed"));
    }
    private sealed class ZipFileGateway(byte[] bytes) : IMoodleSubmissionFileGateway { public Task<SubmissionFileDownloadResult> DownloadFileAsync(string userExternalId, string fileUrl, string filename, long maxBytes, CancellationToken cancellationToken) => Task.FromResult(new SubmissionFileDownloadResult(filename, "application/zip", bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), bytes, false)); }
    private sealed class User(string subject = "teacher", bool isAdmin = false) : ICurrentUserContext { public string Subject => subject; public string? Email => null; public IReadOnlyCollection<string> Scopes => isAdmin ? ["grading.admin"] : []; public bool HasScope(string scope) => isAdmin && string.Equals(scope, "grading.admin", StringComparison.Ordinal); }
    private sealed class Audits : IMoodleAuditLogRepository { public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken) => Task.CompletedTask; public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken) => Task.FromResult(0); public Task<int> CountByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken) => Task.FromResult(0); public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(Guid batchJobId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]); public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(string correlationId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]); public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask; }
}
