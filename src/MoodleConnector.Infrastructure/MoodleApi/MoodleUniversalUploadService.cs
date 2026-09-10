using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleUniversalUploadService(
    IMoodleResourceGateway resources,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleDraftUploadGateway uploadGateway,
    IPendingActionService pendingActions,
    IActionConfirmationService confirmations,
    IPendingMoodleActionRepository pendingActionRepository,
    IMoodleAuditLogRepository auditLogs,
    IOptions<MoodleUniversalApiFeatureOptions> features,
    IOptions<GradingLimitsOptions> limits,
    ICurrentUserContext currentUser,
    IMoodleConnectionSelection connectionSelection) : IMoodleUniversalUploadService
{
    private static readonly TimeSpan PendingActionExpiration = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MoodleUploadPreview> PrepareAsync(
        string resourceUri,
        string? filename,
        string? mimeType,
        string filePath,
        int? itemId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        EnsureWriteScope();
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        if (!credentials.CanWrite)
        {
            throw new MoodleApiException("write_not_allowed", "A conexao Moodle selecionada nao permite upload.");
        }

        var resource = await resources.ReadAsync(resourceUri, cancellationToken);
        var safeFilename = NormalizeFilename(filename, resource.Uri);
        var safeMimeType = NormalizeMimeType(mimeType, resource.MimeType);
        EnsureAllowedMimeType(safeMimeType);
        var maxBytes = Math.Clamp(limits.Value.MaxFileSizeMb, 1, 100) * 1024L * 1024L;
        var actualSizeBytes = resource.Content.LongLength;
        if (resource.SizeBytes != actualSizeBytes)
        {
            throw new MoodleApiException(
                "resource_integrity_mismatch",
                "O tamanho declarado do resource nao corresponde ao conteudo binario lido; prepare um novo resource.");
        }

        if (actualSizeBytes > maxBytes)
        {
            throw new MoodleApiException("file_too_large", "O arquivo excede o limite configurado para upload.");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(resource.Content)).ToLowerInvariant();
        var normalizedPath = NormalizeFilePath(filePath);
        var parameterHash = CreateHash(new
        {
            resourceUri,
            credentials.ConnectionId,
            safeFilename,
            safeMimeType,
            actualSizeBytes,
            sha256,
            normalizedPath,
            itemId
        });
        var confirmationText = $"CONFIRMAR UPLOAD MOODLE {safeFilename.ToUpperInvariant()} {parameterHash[..12].ToUpperInvariant()}";
        var preview = new
        {
            operation = "moodle_upload_draft",
            connectionAlias = credentials.Alias,
            resourceUri,
            filename = safeFilename,
            mimeType = safeMimeType,
            sizeBytes = actualSizeBytes,
            sha256,
            filePath = normalizedPath,
            itemId,
            execution = "Nenhuma chamada de upload foi enviada; confirme explicitamente para enviar o binario ao rascunho Moodle."
        };
        var payload = new UniversalMoodleUploadPayload(
            resource.Uri,
            safeFilename,
            safeMimeType,
            actualSizeBytes,
            sha256,
            credentials.ConnectionId,
            credentials.Alias,
            normalizedPath,
            itemId,
            parameterHash);
        var pending = await pendingActions.CreatePendingActionAsync(
            "moodle_prepare_upload",
            ToolRiskLevel.CriticalHumanConfirmedWrite,
            payload,
            preview,
            confirmationText,
            PendingActionExpiration,
            null,
            cancellationToken);
        var action = await pendingActionRepository.GetByIdAsync(pending.PendingActionId, cancellationToken)
            ?? throw new InvalidOperationException("A acao pendente de upload nao foi encontrada apos a preparacao.");
        var now = DateTimeOffset.UtcNow;
        await auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = action.CorrelationId,
            ToolName = "moodle_prepare_upload",
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = action.CreatedBySubject,
            ActorEmail = action.CreatedByEmail,
            ActorMoodleUserId = action.CreatedByMoodleUserId,
            CourseId = action.CourseId,
            MoodleConnectionId = credentials.ConnectionId,
            MoodleConnectionAlias = credentials.Alias,
            PendingActionId = action.Id,
            StartedAt = now,
            FinishedAt = now,
            DurationMs = 0,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new { connectionAlias = credentials.Alias, filename = safeFilename, mimeType = safeMimeType, sizeBytes = actualSizeBytes, sha256 }),
            ResponseSummaryJson = JsonSerializer.Serialize(new { action.Id, action.ExpiresAt }, JsonOptions),
            Status = "upload_prepared"
        }, cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        return new MoodleUploadPreview(
            pending.PendingActionId,
            resource.Uri,
            safeFilename,
            safeMimeType,
            resource.SizeBytes,
            sha256,
            credentials.Alias,
            normalizedPath,
            itemId,
            confirmationText,
            pending.ExpiresAt);
    }

    public async Task<MoodleUploadResult> ConfirmAsync(
        Guid pendingActionId,
        string confirmationText,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        EnsureWriteScope();
        var action = await pendingActionRepository.GetByIdAsync(pendingActionId, cancellationToken)
            ?? throw new InvalidOperationException("Acao pendente de upload nao encontrada.");
        if (!string.Equals(action.ToolName, "moodle_prepare_upload", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A acao pendente nao pertence ao upload universal Moodle.");
        }

        var payload = JsonSerializer.Deserialize<UniversalMoodleUploadPayload>(action.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Os dados da acao pendente de upload estao invalidos.");
        connectionSelection.Alias = payload.ConnectionAlias;
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        if (!credentials.CanWrite)
        {
            throw new MoodleApiException("write_not_allowed", "A conexao Moodle selecionada nao permite upload.");
        }
        if (!string.Equals(credentials.ConnectionId, payload.ConnectionId, StringComparison.Ordinal))
        {
            throw new MoodleApiException("wrong_moodle_alias", "A confirmacao deve usar a mesma conexao Moodle utilizada na previa.");
        }

        var resource = await resources.ReadAsync(payload.ResourceUri, cancellationToken);
        var actualHash = Convert.ToHexString(SHA256.HashData(resource.Content)).ToLowerInvariant();
        if (resource.SizeBytes != payload.SizeBytes || !string.Equals(actualHash, payload.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new MoodleApiException("resource_changed", "O recurso binario mudou desde a previa; prepare um novo upload.");
        }

        var confirmation = await confirmations.ConfirmAsync(
            pendingActionId,
            confirmationText,
            "moodle.write",
            cancellationToken);
        if (confirmation.Status == "already_confirmed")
        {
            return new MoodleUploadResult("already_confirmed", action.Id, [], confirmation.AuditId);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await uploadGateway.UploadAsync(
                credentials,
                new MoodleDraftUploadRequest(payload.Filename, payload.MimeType, resource.Content, payload.FilePath, payload.ItemId),
                cancellationToken);
            action.RecordResult(AuditPayloadSanitizer.SerializeSanitized(new
            {
                operation = "moodle_upload_draft",
                function = "moodle_upload_draft",
                status = "executed",
                files = result.Files,
                payload = result.Payload
            }));
            await RecordAsync(action, payload, "upload_executed", result.Files, stopwatch.ElapsedMilliseconds, null, cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);
            var safeFiles = result.Files
                .Select(file => file with { Url = MoodleContentUrlSanitizer.Sanitize(file.Url) })
                .ToArray();
            return new MoodleUploadResult(
                "executed",
                action.Id,
                safeFiles,
                confirmation.AuditId,
                Payload: AuditPayloadSanitizer.ToSanitizedElement(result.Payload));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var errorCode = exception is MoodleApiException moodle ? moodle.ErrorCode : exception.GetType().Name;
            var unknown = MoodleWriteExecutionClassifier.IsUnknown(exception);
            if (unknown)
            {
                action.MarkExecutionUnknown();
                await pendingActionRepository.SaveChangesAsync(cancellationToken);
            }
            await RecordAsync(action, payload, unknown ? "upload_execution_unknown" : "upload_failed", [], stopwatch.ElapsedMilliseconds, errorCode, cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);
            if (unknown)
            {
                return new MoodleUploadResult("execution_unknown", action.Id, [], confirmation.AuditId);
            }
            throw;
        }
    }

    private async Task RecordAsync(
        PendingMoodleAction action,
        UniversalMoodleUploadPayload payload,
        string status,
        IReadOnlyList<MoodleDraftUploadFile> files,
        long durationMs,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        await auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = action.CorrelationId,
            ToolName = "moodle_confirm_upload",
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = action.CreatedBySubject,
            ActorEmail = action.CreatedByEmail,
            ActorMoodleUserId = action.CreatedByMoodleUserId,
            CourseId = action.CourseId,
            MoodleConnectionId = payload.ConnectionId,
            MoodleConnectionAlias = payload.ConnectionAlias,
            PendingActionId = action.Id,
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            DurationMs = durationMs,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new { payload.ConnectionAlias, payload.Filename, payload.MimeType, payload.SizeBytes, payload.Sha256 }),
            ResponseSummaryJson = JsonSerializer.Serialize(new { files = files.Select(file => new { file.ItemId, file.Filename, file.FilePath, file.SizeBytes }), payload.Sha256 }, JsonOptions),
            Status = status,
            ErrorCode = errorCode
        }, cancellationToken);
    }

    private void EnsureEnabled()
    {
        if (!features.Value.UniversalMoodleFileUploadEnabled)
        {
            throw new InvalidOperationException("O upload universal de arquivos Moodle esta desabilitado.");
        }
    }

    private void EnsureWriteScope()
    {
        if (!currentUser.HasScope("moodle.write"))
        {
            throw new MoodleApiException("moodle_write_scope_required", "O escopo 'moodle.write' e obrigatorio para upload Moodle.");
        }
    }

    private static string NormalizeFilename(string? filename, string resourceUri)
    {
        var fallback = resourceUri.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "arquivo";
        var safe = Path.GetFileName(string.IsNullOrWhiteSpace(filename) ? fallback : filename.Trim());
        return string.IsNullOrWhiteSpace(safe) ? "arquivo" : safe;
    }

    private static string NormalizeMimeType(string? requested, string observed) =>
        (string.IsNullOrWhiteSpace(requested) ? observed : requested).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static string NormalizeFilePath(string? filePath)
    {
        var value = string.IsNullOrWhiteSpace(filePath) ? "/" : filePath.Trim();
        if (value.Contains("..", StringComparison.Ordinal) || value.Contains('\\'))
        {
            throw new ArgumentException("O caminho do arquivo Moodle nao pode conter travessia de diretorio.", nameof(filePath));
        }
        return value.StartsWith('/') ? value : "/" + value;
    }

    private void EnsureAllowedMimeType(string mimeType)
    {
        if (!features.Value.UniversalMoodleAllowedFileMimeTypes.Any(allowed =>
                allowed == "*" ||
                allowed.EndsWith("/*", StringComparison.Ordinal) && mimeType.StartsWith(allowed[..^1], StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowed, mimeType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MoodleApiException("mime_not_allowed", "O tipo MIME do arquivo nao e permitido pela politica de upload.");
        }
    }

    private static string CreateHash(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions)))).ToLowerInvariant();

    private sealed record UniversalMoodleUploadPayload(
        string ResourceUri,
        string Filename,
        string MimeType,
        long SizeBytes,
        string Sha256,
        string ConnectionId,
        string ConnectionAlias,
        string FilePath,
        int? ItemId,
        string ParameterHash);
}
