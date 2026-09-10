using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleUniversalUploadServiceTests
{
    [Fact]
    public async Task PrepareAsync_NaoEnviaUploadERegistraHashDoResource()
    {
        var resource = new FakeResources(Encoding.UTF8.GetBytes("arquivo"));
        var upload = new FakeUploadGateway();
        var pending = new FakePendingActions();
        var sut = CreateService(resource, upload, pending);

        var preview = await sut.PrepareAsync("moodle://resource/1", "resposta.txt", "text/plain", "/", null, CancellationToken.None);

        Assert.Equal(0, upload.Calls);
        Assert.Equal("resposta.txt", preview.Filename);
        Assert.Equal(Hash(resource.Content), preview.Sha256);
        Assert.NotNull(pending.Action);
        Assert.Contains("CONFIRMAR UPLOAD MOODLE", preview.ConfirmationText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmAsync_EnviaUmaVezEDevolveItemIdDoRascunho()
    {
        var resource = new FakeResources(Encoding.UTF8.GetBytes("arquivo"));
        var upload = new FakeUploadGateway();
        var pending = new FakePendingActions();
        var confirmation = new FakeConfirmation();
        var sut = CreateService(resource, upload, pending, confirmation);
        var preview = await sut.PrepareAsync("moodle://resource/1", "resposta.txt", "text/plain", "/", null, CancellationToken.None);

        var result = await sut.ConfirmAsync(preview.PendingActionId, preview.ConfirmationText, CancellationToken.None);

        Assert.Equal("executed", result.Status);
        Assert.Equal(1, upload.Calls);
        Assert.Equal(42, Assert.Single(result.Files).ItemId);
        Assert.Equal("moodle.write", confirmation.LastScope);
        Assert.NotNull(pending.Action!.ResultJson);
        using var persistedResult = JsonDocument.Parse(pending.Action.ResultJson!);
        Assert.Equal("moodle_upload_draft", persistedResult.RootElement.GetProperty("operation").GetString());
        Assert.Equal(42, persistedResult.RootElement.GetProperty("files")[0].GetProperty("itemId").GetInt32());
    }

    [Fact]
    public async Task PrepareAsync_RejeitaTamanhoDeclaradoDiferenteDoConteudo()
    {
        var resource = new FakeResources(Encoding.UTF8.GetBytes("conteudo"), declaredSizeBytes: 1);
        var upload = new FakeUploadGateway();
        var pending = new FakePendingActions();
        var sut = CreateService(resource, upload, pending);

        var exception = await Assert.ThrowsAsync<MoodleApiException>(() =>
            sut.PrepareAsync("moodle://resource/1", "resposta.txt", "text/plain", "/", null, CancellationToken.None));

        Assert.Equal("resource_integrity_mismatch", exception.ErrorCode);
        Assert.Equal(0, upload.Calls);
        Assert.Null(pending.Action);
    }

    private static MoodleUniversalUploadService CreateService(
        FakeResources resources,
        FakeUploadGateway upload,
        FakePendingActions pending,
        FakeConfirmation? confirmation = null) => new(
            resources,
            new FakeCredentialsProvider(),
            upload,
            pending,
            confirmation ?? new FakeConfirmation(),
            pending,
            new FakeAuditLogs(),
            Options.Create(new MoodleUniversalApiFeatureOptions { UniversalMoodleFileUploadEnabled = true }),
            Options.Create(new GradingLimitsOptions { MaxFileSizeMb = 1 }),
            new FakeCurrentUser(),
            new FakeSelection());

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FakeResources(byte[] content, long? declaredSizeBytes = null) : IMoodleResourceGateway
    {
        public byte[] Content { get; } = content;

        public Task<MoodleResourceDescriptor> RegisterAsync(MoodleResourceRegistration request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MoodleResourceDescriptor>> RegisterManyAsync(IReadOnlyList<MoodleResourceRegistration> requests, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<MoodleResourceReadResult> ReadAsync(string uri, CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleResourceReadResult(uri, "text/plain", Content, declaredSizeBytes ?? Content.LongLength, Hash(Content)));
        public Task<IReadOnlyList<MoodleResourceDescriptor>> ExpandZipAsync(string uri, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeUploadGateway : IMoodleDraftUploadGateway
    {
        public int Calls { get; private set; }

        public Task<MoodleDraftUploadResult> UploadAsync(MoodleConnectorCredentials connection, MoodleDraftUploadRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            using var document = JsonDocument.Parse("[{\"itemid\":42,\"filename\":\"resposta.txt\",\"filepath\":\"/\",\"filesize\":7,\"mimetype\":\"text/plain\"}]");
            var file = new MoodleDraftUploadFile(42, "resposta.txt", "/", null, 7, "text/plain");
            return Task.FromResult(new MoodleDraftUploadResult(document.RootElement.Clone(), [file]));
        }
    }

    private sealed class FakeCredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials("client", "connection", "goias", "https://moodle.example", "user", "password", "goias", true));
    }

    private sealed class FakeSelection : IMoodleConnectionSelection
    {
        public string? Alias { get; set; }
    }

    private sealed class FakeCurrentUser : ICurrentUserContext
    {
        public string Subject => "user";
        public string? Email => null;
        public IReadOnlyCollection<string> Scopes => ["moodle.write"];
        public bool HasScope(string requestedScope) => string.Equals(requestedScope, "moodle.write", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakePendingActions : IPendingActionService, IPendingMoodleActionRepository
    {
        public PendingMoodleAction? Action { get; private set; }

        public Task<PendingActionResponse> CreatePendingActionAsync(string toolName, ToolRiskLevel riskLevel, object payload, object preview, string confirmationText, TimeSpan expiresIn, long? courseId, CancellationToken cancellationToken)
        {
            Action = new PendingMoodleAction
            {
                ToolName = toolName,
                RiskLevel = riskLevel,
                CreatedBySubject = "user",
                PayloadJson = JsonSerializer.Serialize(payload),
                PreviewJson = JsonSerializer.Serialize(preview),
                ConfirmationText = confirmationText,
                ExpiresAt = DateTimeOffset.UtcNow.Add(expiresIn),
                CorrelationId = "correlation"
            };
            return Task.FromResult(new PendingActionResponse("pending_confirmation", Action.Id, toolName, riskLevel, preview, confirmationText, Action.ExpiresAt));
        }

        public Task AddAsync(PendingMoodleAction action, CancellationToken cancellationToken) { Action = action; return Task.CompletedTask; }
        public Task<PendingMoodleAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Action?.Id == id ? Action : null);
        public Task<PendingActionConfirmationClaimResult> TryConfirmWithAuditAsync(Guid id, string confirmedBySubject, DateTimeOffset confirmedAt, MoodleAuditLog confirmationAudit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeConfirmation : IActionConfirmationService
    {
        public string? LastScope { get; private set; }
        public Task<ActionConfirmationResponse> ConfirmAsync(Guid pendingActionId, string confirmationText, string? requiredScope, CancellationToken cancellationToken)
        {
            LastScope = requiredScope;
            return Task.FromResult(new ActionConfirmationResponse("confirmed", pendingActionId, "moodle_prepare_upload", ToolRiskLevel.CriticalHumanConfirmedWrite, DateTimeOffset.UtcNow, "audit"));
        }
    }

    private sealed class FakeAuditLogs : IMoodleAuditLogRepository
    {
        public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(string correlationId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);
        public Task<int> CountByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(Guid batchJobId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MoodleAuditLog>>([]);
        public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
