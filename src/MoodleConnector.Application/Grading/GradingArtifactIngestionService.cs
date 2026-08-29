using System.Globalization;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Executa a parte pesada da preparação de um lote em contexto de worker.
/// O request pode deixar apenas referências técnicas; este serviço materializa
/// os artifacts, salva cada transição e nunca persiste bytes do arquivo.
/// </summary>
public sealed class GradingArtifactIngestionService(
    IGradingReviewRepository repository,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleSubmissionFileGateway fileGateway,
    IDocumentExtractionService extractionService,
    IOptions<GradingLimitsOptions> limits,
    ILogger<GradingArtifactIngestionService> logger,
    IGradingOperationTelemetry? telemetry = null) : IGradingArtifactIngestionService
{
    private static readonly HashSet<string> AllowedExtractionMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "text/plain"
    };

    public async Task IngestPendingAsync(
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(item);

        var stopwatch = Stopwatch.StartNew();
        var result = "success";
        var userExternalId = batch.CreatedByMoodleUserId?.ToString(CultureInfo.InvariantCulture)
            ?? batch.CreatedBySubject;
        try
        {
            var changed = false;

            if (batch.IncludeSubmissionFiles)
            {
                changed |= await EnsureSubmissionFileReferencesAsync(
                    userExternalId,
                    item,
                    cancellationToken);
            }

            if (batch.IncludeRubric || batch.IncludeCourseMaterials)
            {
                changed |= await EnsureContextReferencesAsync(
                    userExternalId,
                    batch,
                    item,
                    cancellationToken);
            }

            if (changed)
            {
                // As referências precisam existir antes de o construtor de contexto
                // consultar o repositório, inclusive após um restart do worker.
                await repository.SaveChangesAsync(cancellationToken);
            }

            var artifacts = await repository.ListArtifactsByItemAsync(item.Id, cancellationToken);
            var pendingArtifacts = artifacts.Where(IsPendingDownload).ToArray();
            if (pendingArtifacts.Length > 1)
            {
                await MaterializeConcurrentlyAsync(
                    pendingArtifacts,
                    userExternalId,
                    cancellationToken);
            }
            else
            {
                foreach (var artifact in pendingArtifacts)
                {
                    await MaterializeAsync(artifact, userExternalId, cancellationToken);
                }
            }
        }
        catch
        {
            result = "error";
            throw;
        }
        finally
        {
            telemetry?.RecordPhase(
                "grading",
                "ingestion",
                result,
                stopwatch.Elapsed.TotalMilliseconds,
                queryCount: 1,
                itemCount: 1,
                bytes: 0);
        }
    }

    private async Task MaterializeConcurrentlyAsync(
        IReadOnlyList<GradingArtifact> artifacts,
        string userExternalId,
        CancellationToken cancellationToken)
    {
        var configuredConcurrency = Math.Min(
            limits.Value.FileDownloadWorkers,
            Math.Min(
                limits.Value.MaxConcurrentDownloadsPerConnection,
                limits.Value.MaxConcurrentDownloadsPerBatch));
        var concurrency = Math.Clamp(configuredConcurrency, 1, 16);
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var prepared = await Task.WhenAll(artifacts.Select(async artifact =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await PrepareMaterializationAsync(artifact, userExternalId, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        // O DbContext é scoped e não é thread-safe. Downloads/extração são
        // concorrentes; a projeção persistida é aplicada em ordem, uma por vez.
        foreach (var result in prepared)
        {
            await PersistMaterializationAsync(result, cancellationToken);
        }
    }

    private async Task<bool> EnsureSubmissionFileReferencesAsync(
        string userExternalId,
        AssistedGradingItem item,
        CancellationToken cancellationToken)
    {
        var existing = await repository.ListArtifactsByItemAsync(item.Id, cancellationToken);
        if (existing.Any(artifact => artifact.ArtifactType == "submission_file"))
        {
            return false;
        }

        if (item.SubmissionId is null)
        {
            return false;
        }

        IReadOnlyList<AssignmentSubmissionRecord> submissions;
        try
        {
            submissions = await submissionsGateway.GetAssignmentSubmissionsAsync(
                userExternalId,
                item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                "submitted",
                since: null,
                before: null,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Nao foi possivel recuperar referencias de arquivos da submissao {SubmissionId}.",
                item.SubmissionId);
            return false;
        }

        var match = submissions.FirstOrDefault(submission =>
            string.Equals(
                submission.SubmissionId,
                item.SubmissionId.Value.ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                submission.UserId,
                item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase));
        if (match?.Files is not { Count: > 0 } files)
        {
            return false;
        }

        var maxFiles = Math.Clamp(limits.Value.MaxFilesPerSubmission, 0, 100);
        var selectedFiles = files.Take(maxFiles).ToArray();
        foreach (var file in selectedFiles)
        {
            await repository.AddArtifactAsync(
                BuildPendingArtifact(
                    item.Id,
                    "submission_file",
                    file.Filename,
                    file.MimeType,
                    file.SizeBytes,
                    file.FileUrl),
                cancellationToken);
        }

        return selectedFiles.Length > 0;
    }

    private async Task<bool> EnsureContextReferencesAsync(
        string userExternalId,
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        CancellationToken cancellationToken)
    {
        var existing = await repository.ListArtifactsByItemAsync(item.Id, cancellationToken);
        if (existing.Any(artifact =>
                artifact.ArtifactType == "assignment_context" &&
                !ExtractionStatus.IsFailure(artifact.ExtractionStatus)))
        {
            return false;
        }

        CourseContentsSummary contents;
        try
        {
            contents = await GradingMoodleReadRetry.ExecuteAsync(
                retryCancellationToken => contentsGateway.GetCourseContentsAsync(
                    userExternalId,
                    item.CourseId.ToString(CultureInfo.InvariantCulture),
                    moduleTypes: [],
                    includeHidden: true,
                    onlyWithFiles: false,
                    retryCancellationToken),
                (exception, attempt) => logger.LogWarning(
                    exception,
                    "Falha transitória ao recuperar contexto da tarefa {AssignmentId}; nova tentativa {Attempt}.",
                    item.AssignmentId,
                    attempt),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Nao foi possivel recuperar contexto da tarefa {AssignmentId}.",
                item.AssignmentId);
            await UpsertContextDiagnosticAsync(
                existing,
                item,
                "context_fetch_failed",
                cancellationToken);
            return false;
        }

        var assignmentId = item.AssignmentId.ToString(CultureInfo.InvariantCulture);
        var section = contents.Sections.FirstOrDefault(candidate =>
            candidate.Modules.Any(module => IsAssignmentModule(module, assignmentId)));
        var assignmentModule = section?.Modules.FirstOrDefault(module => IsAssignmentModule(module, assignmentId));
        if (section is null || assignmentModule is null)
        {
            await UpsertContextDiagnosticAsync(
                existing,
                item,
                "context_assignment_not_found",
                cancellationToken);
            return false;
        }

        var added = false;
        if (batch.IncludeRubric && !string.IsNullOrWhiteSpace(assignmentModule.Description))
        {
            await repository.AddArtifactAsync(
                new GradingArtifact(
                    Guid.NewGuid(),
                    item.Id,
                    "assignment_context",
                    assignmentModule.Name,
                    "text/html",
                    Sha256: null,
                    SizeBytes: assignmentModule.Description.Length,
                    ExtractionStatus.Succeeded,
                    assignmentModule.Description,
                    SummaryRef: "assignment_description",
                    DateTimeOffset.UtcNow),
                cancellationToken);
            added = true;
        }

        if (!batch.IncludeCourseMaterials)
        {
            return added;
        }

        var modules = section.Modules.ToArray();
        var assignmentIndex = Array.IndexOf(modules, assignmentModule);
        var nearbyModules = modules
            .Select((module, index) => new
            {
                Module = module,
                Distance = assignmentIndex >= 0 ? Math.Abs(index - assignmentIndex) : int.MaxValue
            })
            .Where(entry => entry.Distance <= 3 && IsContextCandidateModule(entry.Module, assignmentModule))
            .OrderBy(entry => entry.Distance)
            .Take(Math.Max(1, limits.Value.MaxFilesPerSubmission))
            .ToArray();
        var maxContextFiles = Math.Clamp(limits.Value.MaxFilesPerSubmission, 0, 100);
        var contextFilesAdded = 0;

        foreach (var entry in nearbyModules)
        {
            if (!string.IsNullOrWhiteSpace(entry.Module.Description))
            {
                await repository.AddArtifactAsync(
                    new GradingArtifact(
                        Guid.NewGuid(),
                        item.Id,
                        "assignment_context",
                        entry.Module.Name,
                        "text/html",
                        Sha256: null,
                        SizeBytes: entry.Module.Description.Length,
                        ExtractionStatus.Succeeded,
                        entry.Module.Description,
                        SummaryRef: $"section:{section.SectionNumber};distance:{entry.Distance}",
                        DateTimeOffset.UtcNow),
                    cancellationToken);
                added = true;
            }

            foreach (var file in entry.Module.Files.Where(file => !string.IsNullOrWhiteSpace(file.FileUrl)))
            {
                if (contextFilesAdded >= maxContextFiles)
                {
                    break;
                }

                await repository.AddArtifactAsync(
                    BuildPendingArtifact(
                        item.Id,
                        "assignment_context",
                        string.IsNullOrWhiteSpace(file.FileName) ? "context-file" : file.FileName,
                        file.MimeType,
                        file.FileSize,
                        file.FileUrl),
                    cancellationToken);
                added = true;
                contextFilesAdded++;
            }
        }

        return added;
    }

    private async Task UpsertContextDiagnosticAsync(
        IReadOnlyList<GradingArtifact> existing,
        AssistedGradingItem item,
        string reason,
        CancellationToken cancellationToken)
    {
        var diagnostic = existing.FirstOrDefault(artifact =>
            artifact.ArtifactType == "assignment_context" &&
            string.Equals(artifact.SummaryRef, reason, StringComparison.OrdinalIgnoreCase));
        var updated = new GradingArtifact(
            diagnostic?.Id ?? Guid.NewGuid(),
            item.Id,
            "assignment_context",
            $"assignment-{item.AssignmentId.ToString(CultureInfo.InvariantCulture)}",
            null,
            null,
            null,
            ExtractionStatus.Failed,
            null,
            reason,
            diagnostic?.CreatedAt ?? DateTimeOffset.UtcNow);

        if (diagnostic is null)
        {
            await repository.AddArtifactAsync(updated, cancellationToken);
        }
        else
        {
            await repository.UpdateArtifactAsync(updated, cancellationToken);
        }
    }

    private async Task MaterializeAsync(
        GradingArtifact artifact,
        string userExternalId,
        CancellationToken cancellationToken)
    {
        var result = await PrepareMaterializationAsync(artifact, userExternalId, cancellationToken);
        await PersistMaterializationAsync(result, cancellationToken);
    }

    private async Task<MaterializationResult> PrepareMaterializationAsync(
        GradingArtifact artifact,
        string userExternalId,
        CancellationToken cancellationToken)
    {
        var sourceUrl = artifact.SourceUrl;
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return new MaterializationResult(artifact, null, null, null);
        }

        try
        {
            var maxBytes = Math.Max(1, limits.Value.MaxFileSizeMb) * 1024L * 1024L;
            var download = await GradingMoodleReadRetry.ExecuteAsync(
                retryCancellationToken => fileGateway.DownloadFileAsync(
                    userExternalId,
                    sourceUrl,
                    artifact.Filename ?? "arquivo",
                    maxBytes,
                    retryCancellationToken),
                (exception, attempt) => logger.LogWarning(
                    exception,
                    "Falha transitória ao baixar artifact {ArtifactId}; nova tentativa {Attempt}.",
                    artifact.Id,
                    attempt),
                cancellationToken);
            if (!AllowedExtractionMimeTypes.Contains(download.MimeType))
            {
                throw new InvalidOperationException("O tipo MIME do arquivo não é permitido para extração acadêmica.");
            }
            var extraction = await extractionService.ExtractAsync(
                download.Filename,
                download.MimeType,
                download.Content,
                cancellationToken);
            return new MaterializationResult(artifact, download, extraction, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Falha ao materializar artifact {ArtifactId} do item {GradingItemId}.",
                artifact.Id,
                artifact.GradingItemId);
            return new MaterializationResult(artifact, null, null, ex);
        }
    }

    private async Task PersistMaterializationAsync(
        MaterializationResult result,
        CancellationToken cancellationToken)
    {
        if (result.Error is not null)
        {
            await repository.UpdateArtifactAsync(
                result.Artifact with
                {
                    ExtractionStatus = ExtractionStatus.Failed,
                    ExtractedTextRef = null,
                    SummaryRef = result.Artifact.ArtifactType == "assignment_context"
                        ? "context_materialization_failed"
                        : "ingestion_failed",
                    SourceUrl = null
                },
                cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        if (result.Download is null || result.Extraction is null)
        {
            return;
        }

        await repository.UpdateArtifactAsync(
            result.Artifact with
            {
                Filename = result.Download.Filename,
                MimeType = result.Download.MimeType,
                Sha256 = result.Download.Sha256Hex,
                SizeBytes = result.Download.SizeBytes,
                ExtractionStatus = result.Extraction.ExtractionStatus,
                ExtractedTextRef = result.Extraction.ExtractedText,
                SummaryRef = result.Extraction.ErrorMessage,
                SourceUrl = null
            },
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    private sealed record MaterializationResult(
        GradingArtifact Artifact,
        SubmissionFileDownloadResult? Download,
        DocumentExtractionResult? Extraction,
        Exception? Error);

    private static bool IsPendingDownload(GradingArtifact artifact) =>
        artifact.ExtractionStatus == ExtractionStatus.Pending &&
        !string.IsNullOrWhiteSpace(artifact.SourceUrl);

    private static GradingArtifact BuildPendingArtifact(
        Guid gradingItemId,
        string artifactType,
        string? filename,
        string? mimeType,
        long? sizeBytes,
        string? sourceUrl)
    {
        var normalizedSource = GradingArtifactSourceReference.Normalize(sourceUrl);
        return new GradingArtifact(
            Guid.NewGuid(),
            gradingItemId,
            artifactType,
            filename,
            mimeType,
            Sha256: null,
            sizeBytes,
            normalizedSource is null ? ExtractionStatus.Failed : ExtractionStatus.Pending,
            ExtractedTextRef: null,
            SummaryRef: normalizedSource is null ? "source_url_invalid" : "pending_ingestion",
            DateTimeOffset.UtcNow,
            normalizedSource);
    }

    private static bool IsAssignmentModule(CourseModuleSummary module, string assignmentId) =>
        string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(module.InstanceId, assignmentId, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(module.ModuleId, assignmentId, StringComparison.OrdinalIgnoreCase));

    private static bool IsContextCandidateModule(CourseModuleSummary module, CourseModuleSummary assignmentModule)
    {
        if (ReferenceEquals(module, assignmentModule))
        {
            return false;
        }

        if (module.Files.Count > 0)
        {
            return true;
        }

        return module.ModuleType.Equals("resource", StringComparison.OrdinalIgnoreCase) ||
            module.ModuleType.Equals("page", StringComparison.OrdinalIgnoreCase) ||
            module.ModuleType.Equals("label", StringComparison.OrdinalIgnoreCase) ||
            module.ModuleType.Equals("folder", StringComparison.OrdinalIgnoreCase);
    }
}
