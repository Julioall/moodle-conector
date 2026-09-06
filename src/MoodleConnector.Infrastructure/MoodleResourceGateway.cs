using System.Security.Cryptography;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleResourceGateway(
    IMoodleResourceRepository repository,
    IMoodleSubmissionFileGateway fileGateway,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    ICurrentUserContext currentUser,
    IOptions<GradingLimitsOptions> limits,
    IOptions<MoodleUniversalApiFeatureOptions> features,
    IMoodleAuditLogRepository auditLogs,
    IMemoryCache cache,
    IGradingOperationTelemetry? telemetry = null) : IMoodleResourceGateway
{
    public async Task<MoodleResourceDescriptor> RegisterAsync(MoodleResourceRegistration request, CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        if (string.IsNullOrWhiteSpace(request.Filename) || string.IsNullOrWhiteSpace(request.RemoteFileReference))
            throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "O arquivo Moodle nao possui referencia valida.");
        if (string.IsNullOrWhiteSpace(currentUser.Subject))
            throw new MoodleResourceException("RESOURCE_FORBIDDEN", "O usuario autenticado e obrigatorio para registrar o resource Moodle.");
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var remoteReference = SanitizeRemoteReference(request.RemoteFileReference);
        var now = DateTimeOffset.UtcNow;
        var reusable = await repository.FindReusableAsync(
            credentials.ClientId,
            credentials.ConnectionId,
            currentUser.Subject.Trim(),
            request,
            remoteReference,
            now,
            cancellationToken);
        if (reusable is not null)
        {
            return new MoodleResourceDescriptor(
                MoodleResourceUri.Create(reusable.ResourceId),
                reusable.Filename,
                reusable.MimeType,
                reusable.SizeBytes,
                reusable.Sha256);
        }
        var resource = new MoodleResource
        {
            ClientId = credentials.ClientId,
            ConnectionId = credentials.ConnectionId,
            MoodleAlias = credentials.Alias,
            OwnerSubject = currentUser.Subject.Trim(),
            ResourceType = string.IsNullOrWhiteSpace(request.ResourceType) ? "submission_attachment" : request.ResourceType.Trim(),
            CourseId = request.CourseId,
            AssignmentId = request.AssignmentId,
            SubmissionId = request.SubmissionId,
            StudentId = request.StudentId,
            ContextId = request.ContextId,
            Component = request.Component,
            FileArea = request.FileArea,
            ItemId = request.ItemId,
            Filename = Path.GetFileName(request.Filename.Trim()),
            MimeType = NormalizeMimeType(request.MimeType),
            SizeBytes = request.SizeBytes,
            Sha256 = NormalizeHash(request.Sha256),
            RemoteFileReference = remoteReference,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(Math.Clamp(limits.Value.ResourceExpirationMinutes, 1, 24 * 60))
        };
        await repository.RegisterAsync(resource, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await AuditAsync(resource, "registered", null, now, cancellationToken, "moodle_resource_register");
        telemetry?.RecordPhase("moodle_resource", "register", "success", 0, itemCount: 1);
        return new MoodleResourceDescriptor(MoodleResourceUri.Create(resource.ResourceId), resource.Filename, resource.MimeType, resource.SizeBytes, resource.Sha256);
    }

    public async Task<IReadOnlyList<MoodleResourceDescriptor>> RegisterManyAsync(
        IReadOnlyList<MoodleResourceRegistration> requests,
        CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        if (requests.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(currentUser.Subject))
        {
            throw new MoodleResourceException("RESOURCE_FORBIDDEN", "O usuario autenticado e obrigatorio para registrar o resource Moodle.");
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var expiration = TimeSpan.FromMinutes(Math.Clamp(limits.Value.ResourceExpirationMinutes, 1, 24 * 60));
        var descriptors = new MoodleResourceDescriptor[requests.Count];
        var pending = new List<(int Index, string CacheKey, MoodleResourceRegistration Request, MoodleResource Resource)>();
        var lookupRequests = new List<(int Index, string CacheKey, MoodleResourceRegistration Request)>();

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (string.IsNullOrWhiteSpace(request.Filename) || string.IsNullOrWhiteSpace(request.RemoteFileReference))
            {
                throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "O arquivo Moodle nao possui referencia valida.");
            }

            var remoteReference = SanitizeRemoteReference(request.RemoteFileReference);
            var cacheKey = BuildRegistrationCacheKey(credentials.ClientId, credentials.ConnectionId, currentUser.Subject, request, remoteReference);
            if (cache.TryGetValue(cacheKey, out MoodleResourceDescriptor? cached) && cached is not null)
            {
                descriptors[index] = cached;
                continue;
            }
            var normalizedRequest = request with { RemoteFileReference = remoteReference };
            lookupRequests.Add((index, cacheKey, normalizedRequest));

            var resource = new MoodleResource
            {
                ClientId = credentials.ClientId,
                ConnectionId = credentials.ConnectionId,
                MoodleAlias = credentials.Alias,
                OwnerSubject = currentUser.Subject.Trim(),
                ResourceType = string.IsNullOrWhiteSpace(request.ResourceType) ? "submission_attachment" : request.ResourceType.Trim(),
                CourseId = request.CourseId,
                AssignmentId = request.AssignmentId,
                SubmissionId = request.SubmissionId,
                StudentId = request.StudentId,
                ContextId = request.ContextId,
                Component = request.Component,
                FileArea = request.FileArea,
                ItemId = request.ItemId,
                Filename = Path.GetFileName(request.Filename.Trim()),
                MimeType = NormalizeMimeType(request.MimeType),
                SizeBytes = request.SizeBytes,
                Sha256 = NormalizeHash(request.Sha256),
                RemoteFileReference = remoteReference,
                CreatedAt = now,
                ExpiresAt = now.Add(expiration)
            };
            pending.Add((index, cacheKey, normalizedRequest, resource));
        }

        if (lookupRequests.Count > 0)
        {
            var reusableByKey = (await repository.FindReusableManyAsync(
                    credentials.ClientId,
                    credentials.ConnectionId,
                    currentUser.Subject.Trim(),
                    lookupRequests.Select(entry => entry.Request).ToArray(),
                    now,
                    cancellationToken))
                .GroupBy(resource => BuildRegistrationCacheKey(
                    credentials.ClientId,
                    credentials.ConnectionId,
                    currentUser.Subject,
                    resource),
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var lookup in lookupRequests)
            {
                if (!reusableByKey.TryGetValue(lookup.CacheKey, out var reusable))
                {
                    continue;
                }

                var reusableDescriptor = new MoodleResourceDescriptor(
                    MoodleResourceUri.Create(reusable.ResourceId),
                    reusable.Filename,
                    reusable.MimeType,
                    reusable.SizeBytes,
                    reusable.Sha256);
                descriptors[lookup.Index] = reusableDescriptor;
                cache.Set(lookup.CacheKey, reusableDescriptor, new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = reusable.ExpiresAt
                });
            }

            pending = pending
                .Where(entry => descriptors[entry.Index] is null)
                .ToList();
        }

        // A page can contain the same attachment more than once when the
        // caller retries. De-duplicate by identity before touching the DB.
        var uniquePending = pending
            .GroupBy(entry => entry.CacheKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (uniquePending.Length > 0)
        {
            await repository.RegisterManyAsync(uniquePending.Select(entry => entry.Resource).ToArray(), cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }

        foreach (var entry in uniquePending)
        {
            var descriptor = new MoodleResourceDescriptor(
                MoodleResourceUri.Create(entry.Resource.ResourceId),
                entry.Resource.Filename,
                entry.Resource.MimeType,
                entry.Resource.SizeBytes,
                entry.Resource.Sha256);
            cache.Set(entry.CacheKey, descriptor, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = entry.Resource.ExpiresAt
            });
            foreach (var duplicate in pending.Where(candidate => string.Equals(candidate.CacheKey, entry.CacheKey, StringComparison.Ordinal)))
            {
                descriptors[duplicate.Index] = descriptor;
            }
        }

        foreach (var entry in uniquePending)
        {
            await auditLogs.AddAsync(new MoodleAuditLog
            {
                CorrelationId = Guid.NewGuid().ToString("N"),
                ToolName = "moodle_resource_register_many",
                RiskLevel = ToolRiskLevel.SensitiveRead,
                ActorSubject = currentUser.Subject,
                CourseId = entry.Resource.CourseId,
                MoodleConnectionId = entry.Resource.ConnectionId,
                MoodleConnectionAlias = entry.Resource.MoodleAlias,
                StartedAt = now,
                FinishedAt = DateTimeOffset.UtcNow,
                DurationMs = 0,
                RequestSanitizedJson = System.Text.Json.JsonSerializer.Serialize(new { resourceType = entry.Resource.ResourceType }),
                ResponseSummaryJson = System.Text.Json.JsonSerializer.Serialize(new { entry.Resource.Filename, entry.Resource.SizeBytes, entry.Resource.Sha256 }),
                Status = "registered"
            }, cancellationToken);
        }
        await auditLogs.SaveChangesAsync(cancellationToken);
        telemetry?.RecordPhase("moodle_resource", "register_many", "success", 0, itemCount: uniquePending.Length);
        return descriptors;
    }

    public async Task<MoodleResourceReadResult> ReadAsync(string uri, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        MoodleResource? resource = null;
        try
        {
            EnsureFeatureEnabled();
            if (!MoodleResourceUri.TryParse(uri, out var resourceId))
                throw new MoodleResourceException("INVALID_RESOURCE_URI", "A URI de resource Moodle e invalida.");
            resource = await repository.FindAsync(resourceId, cancellationToken)
                ?? throw new MoodleResourceException("RESOURCE_NOT_FOUND", "O resource Moodle nao foi encontrado.");
            if (resource.IsExpired(DateTimeOffset.UtcNow))
                throw new MoodleResourceException("RESOURCE_EXPIRED", "O resource Moodle expirou.");

        // The opaque id is never an authorization grant. Resolve the active
        // connection again on every read and bind it to its owner and identity.
            var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
            if (!string.Equals(resource.ClientId, credentials.ClientId, StringComparison.Ordinal) ||
                !string.Equals(resource.ConnectionId, credentials.ConnectionId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(currentUser.Subject) ||
                (!string.Equals(resource.OwnerSubject, currentUser.Subject, StringComparison.Ordinal) &&
                 !currentUser.HasScope("grading.admin") &&
                 !currentUser.HasPlatformPermission("tool.assignments.grade")))
                throw new MoodleResourceException("RESOURCE_FORBIDDEN", "O usuario nao possui acesso a este resource Moodle.");

            if (resource.InlineContent is not null)
            {
                var inlineHash = Convert.ToHexString(SHA256.HashData(resource.InlineContent)).ToLowerInvariant();
                if (!string.Equals(resource.Sha256, inlineHash, StringComparison.OrdinalIgnoreCase)) throw new MoodleResourceException("RESOURCE_HASH_MISMATCH", "O resource extraido perdeu integridade.");
                await AuditAsync(resource, "success_inline", null, startedAt, cancellationToken);
                return new MoodleResourceReadResult(MoodleResourceUri.Create(resource.ResourceId), resource.MimeType, resource.InlineContent, resource.InlineContent.LongLength, inlineHash);
            }

            var maxBytes = Math.Max(1, limits.Value.MaxResourceBytes);
            if (resource.SizeBytes is > 0 && resource.SizeBytes > maxBytes)
                throw new MoodleResourceException("RESOURCE_TOO_LARGE", "O arquivo excede o limite configurado para resources.");
            var cacheKey = string.IsNullOrWhiteSpace(resource.Sha256) ? null : $"moodle-resource:{resource.ClientId}:{resource.ConnectionId}:{resource.Sha256}";
            if (cacheKey is not null && cache.TryGetValue(cacheKey, out CachedResource? cached) && cached is not null)
            {
                await AuditAsync(resource, "success_cache", null, startedAt, cancellationToken);
                telemetry?.RecordPhase("moodle_resource", "read", "cache_hit", (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, itemCount: 1, bytes: cached.Content.LongLength);
                return new MoodleResourceReadResult(MoodleResourceUri.Create(resource.ResourceId), cached.MimeType, cached.Content, cached.Content.LongLength, cached.Sha256);
            }
            // Emite cache_miss apenas quando havia chave (sha256 conhecido) mas não havia entrada.
            if (cacheKey is not null)
                telemetry?.RecordPhase("moodle_resource", "read", "cache_miss", 0);
            SubmissionFileDownloadResult download;
            var downloadStartedAt = DateTimeOffset.UtcNow;
            try { download = await fileGateway.DownloadFileAsync(currentUser.Subject, resource.RemoteFileReference, resource.Filename, maxBytes, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                telemetry?.RecordPhase("moodle_resource", "download", "RESOURCE_DOWNLOAD_FAILED", (DateTimeOffset.UtcNow - downloadStartedAt).TotalMilliseconds);
                throw new MoodleResourceException("RESOURCE_DOWNLOAD_FAILED", "Nao foi possivel baixar o resource Moodle.", exception);
            }
            var downloadDurationMs = (DateTimeOffset.UtcNow - downloadStartedAt).TotalMilliseconds;
            telemetry?.RecordPhase("moodle_resource", "download", "success", downloadDurationMs, itemCount: 1, bytes: download.SizeBytes);
            if (download.Truncated || download.SizeBytes > maxBytes) throw new MoodleResourceException("RESOURCE_TOO_LARGE", "O arquivo excede o limite configurado para resources.");
            // Este gateway transporta o binario original. MIME, extensao e
            // assinatura sao metadados do arquivo, nao uma whitelist de
            // formatos que o conector precise interpretar.
            if (!string.IsNullOrWhiteSpace(resource.Sha256) && !string.Equals(resource.Sha256, download.Sha256Hex, StringComparison.OrdinalIgnoreCase)) throw new MoodleResourceException("RESOURCE_HASH_MISMATCH", "O arquivo Moodle foi alterado desde o registro do resource.");
            resource.RecordIntegrity(download.MimeType, download.SizeBytes, download.Sha256Hex);
            await repository.SaveChangesAsync(cancellationToken);
            cache.Set($"moodle-resource:{resource.ClientId}:{resource.ConnectionId}:{download.Sha256Hex}", new CachedResource(download.Content, download.MimeType, download.Sha256Hex), resource.ExpiresAt);
            await AuditAsync(resource, "success", null, startedAt, cancellationToken);
            telemetry?.RecordPhase("moodle_resource", "read", "success", (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, itemCount: 1, bytes: download.SizeBytes);
            return new MoodleResourceReadResult(MoodleResourceUri.Create(resource.ResourceId), download.MimeType, download.Content, download.SizeBytes, download.Sha256Hex);
        }
        catch (MoodleResourceException exception)
        {
            await AuditAsync(resource, "error", exception.ErrorCode, startedAt, CancellationToken.None);
            telemetry?.RecordPhase("moodle_resource", "read", exception.ErrorCode, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, itemCount: 1);
            throw;
        }
    }

    public async Task<IReadOnlyList<MoodleResourceDescriptor>> ExpandZipAsync(string uri, CancellationToken cancellationToken)
    {
        if (!features.Value.McpResourceZipEnabled) throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "O processamento ZIP por MCP Resource esta desabilitado.");
        if (!MoodleResourceUri.TryParse(uri, out var parentId)) throw new MoodleResourceException("INVALID_RESOURCE_URI", "A URI de resource Moodle e invalida.");
        var parent = await repository.FindAsync(parentId, cancellationToken) ?? throw new MoodleResourceException("RESOURCE_NOT_FOUND", "O resource Moodle nao foi encontrado.");
        var zip = await ReadAsync(uri, cancellationToken);
        if (!IsZip(parent.MimeType) && !IsZip(zip.MimeType)) throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "O resource informado nao e um ZIP.");
        try
        {
            using var stream = new MemoryStream(zip.Content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count > Math.Max(1, limits.Value.MaxZipEntries)) throw new MoodleResourceException("RESOURCE_TOO_LARGE", "O ZIP excede o limite de entradas.");
            var maxExtracted = Math.Max(1, limits.Value.MaxExtractedZipBytes);
            long extracted = 0;
            var children = new List<MoodleResourceDescriptor>();
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.FullName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(entry.FullName) || entry.FullName.Contains(':')) throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "O ZIP contem caminho inseguro.");
                if (Path.GetExtension(entry.Name).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "ZIPs aninhados nao sao suportados.");
                if (entry.Length < 0 || entry.Length > maxExtracted - extracted) throw new MoodleResourceException("RESOURCE_TOO_LARGE", "O ZIP excede o limite extraido configurado.");
                await using var input = entry.Open();
                var content = await ReadZipEntryBoundedAsync(input, maxExtracted - extracted, cancellationToken);
                extracted += content.LongLength;
                var mimeType = MimeFromFilename(entry.Name);
                if (!IsSupportedMime(mimeType) || !HasValidSignature(mimeType, entry.Name, content)) throw new MoodleResourceException("RESOURCE_UNSUPPORTED", $"Arquivo ZIP nao suportado: {entry.Name}.");
                var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                var child = new MoodleResource { ClientId = parent.ClientId, ConnectionId = parent.ConnectionId, MoodleAlias = parent.MoodleAlias, OwnerSubject = parent.OwnerSubject, ResourceType = "submission_zip_entry", CourseId = parent.CourseId, AssignmentId = parent.AssignmentId, SubmissionId = parent.SubmissionId, StudentId = parent.StudentId, Filename = Path.GetFileName(entry.Name), MimeType = mimeType, SizeBytes = content.LongLength, Sha256 = hash, RemoteFileReference = parent.RemoteFileReference, ParentResourceId = parent.ResourceId, InlineContent = content, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = parent.ExpiresAt };
                await repository.RegisterAsync(child, cancellationToken);
                children.Add(new MoodleResourceDescriptor(MoodleResourceUri.Create(child.ResourceId), child.Filename, child.MimeType, child.SizeBytes, child.Sha256));
            }
            await repository.SaveChangesAsync(cancellationToken);
            return children;
        }
        catch (InvalidDataException exception) { throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "O arquivo ZIP esta corrompido.", exception); }
    }

    private static string SanitizeRemoteReference(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "A referencia remota do arquivo e invalida.");
        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty, UserName = string.Empty, Password = string.Empty };
        return builder.Uri.AbsoluteUri;
    }
    private static string NormalizeMimeType(string? value) => string.IsNullOrWhiteSpace(value) ? "application/octet-stream" : value.Trim().ToLowerInvariant();
    private static string? NormalizeHash(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    private bool IsSupportedMime(string value) =>
        value.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("officedocument", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("text/csv", StringComparison.OrdinalIgnoreCase) ||
        features.Value.McpResourceZipEnabled && IsZip(value);
    private static bool IsCompatibleMime(string declared, string observed, string filename)
    {
        if (string.Equals(declared, "application/octet-stream", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(declared, observed, StringComparison.OrdinalIgnoreCase)) return false;
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => observed.Equals("application/pdf", StringComparison.OrdinalIgnoreCase),
            ".png" => observed.Equals("image/png", StringComparison.OrdinalIgnoreCase),
            ".jpg" or ".jpeg" => observed.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase),
            ".docx" => observed.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase),
            ".xlsx" => observed.Equals("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase),
            ".pptx" => observed.Equals("application/vnd.openxmlformats-officedocument.presentationml.presentation", StringComparison.OrdinalIgnoreCase),
            ".json" => observed.Equals("application/json", StringComparison.OrdinalIgnoreCase),
            ".csv" => observed.Equals("text/csv", StringComparison.OrdinalIgnoreCase),
            _ => !observed.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
                 !observed.Equals("image/png", StringComparison.OrdinalIgnoreCase) &&
                 !observed.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                 !observed.Contains("officedocument", StringComparison.OrdinalIgnoreCase) &&
                 !observed.Equals("application/json", StringComparison.OrdinalIgnoreCase) &&
                 !observed.Equals("text/csv", StringComparison.OrdinalIgnoreCase)
        };
    }
    private static bool HasValidSignature(string mimeType, string filename, byte[] content)
    {
        if (content.Length == 0) return false;
        if (mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)) return content.Length >= 5 && content.AsSpan(0, 5).SequenceEqual("%PDF-"u8);
        if (mimeType.Equals("image/png", StringComparison.OrdinalIgnoreCase)) return content.Length >= 8 && content.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)) return content.Length >= 3 && content[0] == 0xff && content[1] == 0xd8 && content[2] == 0xff;
        if (mimeType.Contains("officedocument", StringComparison.OrdinalIgnoreCase)) return content.Length >= 4 && content.AsSpan(0, 4).SequenceEqual("PK\x03\x04"u8);
        if (IsZip(mimeType)) return content.Length >= 4 && content.AsSpan(0, 4).SequenceEqual("PK\x03\x04"u8);
        return !content.Contains((byte)0);
    }
    private static bool IsZip(string mimeType) => mimeType.Equals("application/zip", StringComparison.OrdinalIgnoreCase) || mimeType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase);
    private static string MimeFromFilename(string filename) => Path.GetExtension(filename).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".json" => "application/json", ".csv" => "text/csv", _ => "text/plain" };

    private static async Task<byte[]> ReadZipEntryBoundedAsync(Stream input, long remaining, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream((int)Math.Min(remaining, 1024 * 1024));
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length > remaining - read) throw new MoodleResourceException("RESOURCE_TOO_LARGE", "O ZIP excede o limite extraido configurado.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private void EnsureFeatureEnabled()
    {
        if (!features.Value.McpResourceSubmissionDeliveryEnabled)
            throw new MoodleResourceException("RESOURCE_UNSUPPORTED", "A entrega de submissao por MCP Resource esta desabilitada.");
    }

    private static string BuildRegistrationCacheKey(
        string clientId,
        string connectionId,
        string ownerSubject,
        MoodleResourceRegistration request,
        string remoteReference) =>
        $"moodle-resource-registration:{CachePart(clientId)}:{CachePart(connectionId)}:{CachePart(ownerSubject)}:{CachePart(NormalizeResourceType(request.ResourceType))}:{request.CourseId}:{request.AssignmentId}:{request.SubmissionId}:{request.StudentId}:{CachePart(Path.GetFileName(request.Filename.Trim()))}:{CachePart(remoteReference)}:{CachePart(NormalizeHash(request.Sha256))}";

    private static string BuildRegistrationCacheKey(
        string clientId,
        string connectionId,
        string ownerSubject,
        MoodleResource resource) =>
        $"moodle-resource-registration:{CachePart(clientId)}:{CachePart(connectionId)}:{CachePart(ownerSubject)}:{CachePart(NormalizeResourceType(resource.ResourceType))}:{resource.CourseId}:{resource.AssignmentId}:{resource.SubmissionId}:{resource.StudentId}:{CachePart(resource.Filename)}:{CachePart(resource.RemoteFileReference)}:{CachePart(NormalizeHash(resource.Sha256))}";

    private static string CachePart(string? value) =>
        Uri.EscapeDataString(string.IsNullOrWhiteSpace(value) ? "_" : value.Trim());

    private static string NormalizeResourceType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "submission_attachment" : value.Trim();

    private async Task AuditAsync(MoodleResource? resource, string status, string? errorCode, DateTimeOffset startedAt, CancellationToken cancellationToken, string toolName = "moodle_resource_read")
    {
        await auditLogs.AddAsync(new MoodleAuditLog { CorrelationId = Guid.NewGuid().ToString("N"), ToolName = toolName, RiskLevel = ToolRiskLevel.SensitiveRead, ActorSubject = string.IsNullOrWhiteSpace(currentUser.Subject) ? "unknown" : currentUser.Subject, CourseId = resource?.CourseId, MoodleConnectionId = resource?.ConnectionId, MoodleConnectionAlias = resource?.MoodleAlias, StartedAt = startedAt, FinishedAt = DateTimeOffset.UtcNow, DurationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, RequestSanitizedJson = System.Text.Json.JsonSerializer.Serialize(new { resourceId = resource?.ResourceId, resourceType = resource?.ResourceType }), ResponseSummaryJson = System.Text.Json.JsonSerializer.Serialize(new { resource?.Filename, resource?.MimeType, resource?.SizeBytes, resource?.Sha256 }), Status = status, ErrorCode = errorCode }, cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);
    }

    private sealed record CachedResource(byte[] Content, string MimeType, string Sha256);
}
