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
/// Completa as referências de um lote em contexto de worker.
/// A correção usa somente MCP Resources: este serviço não baixa, interpreta ou
/// persiste conteúdo de arquivos.
/// </summary>
public sealed class GradingArtifactIngestionService(
    IGradingReviewRepository repository,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IOptions<GradingLimitsOptions> limits,
    ILogger<GradingArtifactIngestionService> logger,
    IGradingOperationTelemetry? telemetry = null) : IGradingArtifactIngestionService
{
    // One worker scope handles one child batch sequentially. Cache repeated
    // Moodle reads by assignment/course so a 400-item batch does not issue
    // hundreds of identical requests when references were initially empty.
    private readonly Dictionary<long, IReadOnlyList<AssignmentSubmissionRecord>> submissionFilesCache = [];
    private readonly Dictionary<long, (CourseContentsSummary? Contents, string? Error)> courseContentsCache = [];
    private readonly Dictionary<Guid, IReadOnlyList<GradingArtifact>> artifactsCache = [];

    public IReadOnlyList<GradingArtifact>? TryGetCachedArtifacts(Guid gradingItemId) =>
        artifactsCache.GetValueOrDefault(gradingItemId);

    public async Task PrepareBatchAsync(
        AssistedGradingBatch batch,
        IReadOnlyCollection<Guid> gradingItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (gradingItemIds.Count == 0)
        {
            return;
        }

        var loaded = await repository.ListArtifactsByItemsAsync(gradingItemIds, cancellationToken);
        foreach (var itemId in gradingItemIds)
        {
            artifactsCache[itemId] = loaded.GetValueOrDefault(itemId, []);
        }
    }

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
            if (batch.IncludeSubmissionFiles)
            {
                await EnsureSubmissionFileReferencesAsync(
                    userExternalId,
                    item,
                    cancellationToken);
            }

            if (batch.IncludeRubric || batch.IncludeCourseMaterials)
            {
                await EnsureContextReferencesAsync(
                    userExternalId,
                    batch,
                    item,
                    cancellationToken);
            }

            // O worker persiste o conjunto de alterações em checkpoints
            // amortizados. Salvar aqui por item transformava um lote de
            // 10.000 correções em milhares de transações; se o processo
            // cair antes do checkpoint, o item permanece Pending e pode
            // ser reidratado de forma idempotente no retry.

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

    private async Task<bool> EnsureSubmissionFileReferencesAsync(
        string userExternalId,
        AssistedGradingItem item,
        CancellationToken cancellationToken)
    {
        var existing = await GetExistingArtifactsAsync(item.Id, cancellationToken);
        if (existing.Any(artifact => artifact.ArtifactType == "submission_file"))
        {
            return false;
        }

        if (item.SubmissionId is null)
        {
            return false;
        }

        if (!submissionFilesCache.TryGetValue(item.AssignmentId, out var submissions))
        {
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
                // Cache the failed read for this batch. A later batch retry can
                // recover it, while siblings do not hammer an unavailable
                // Moodle endpoint in the same run.
                submissions = [];
            }

            submissionFilesCache[item.AssignmentId] = submissions;
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
            var artifact = BuildPendingArtifact(
                item.Id,
                "submission_file",
                file.Filename,
                file.MimeType,
                file.SizeBytes,
                file.FileUrl);
            await repository.AddArtifactAsync(artifact, cancellationToken);
            AppendCachedArtifact(artifact);
        }

        return selectedFiles.Length > 0;
    }

    private async Task<bool> EnsureContextReferencesAsync(
        string userExternalId,
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        CancellationToken cancellationToken)
    {
        var existing = await GetExistingArtifactsAsync(item.Id, cancellationToken);
        if (existing.Any(artifact =>
                artifact.ArtifactType == "assignment_context" &&
                !ExtractionStatus.IsFailure(artifact.ExtractionStatus)))
        {
            return false;
        }

        if (!courseContentsCache.TryGetValue(item.CourseId, out var cachedContents))
        {
            try
            {
                var fetchedContents = await GradingMoodleReadRetry.ExecuteAsync(
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
                cachedContents = (fetchedContents, null);
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
                cachedContents = (null, ex.GetType().Name);
            }

            courseContentsCache[item.CourseId] = cachedContents;
        }

        if (cachedContents.Contents is null)
        {
            await UpsertContextDiagnosticAsync(
                existing,
                item,
                "context_fetch_failed",
                cancellationToken);
            return false;
        }

        var contents = cachedContents.Contents;

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
            var artifact = new GradingArtifact(
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
                DateTimeOffset.UtcNow);
            await repository.AddArtifactAsync(artifact, cancellationToken);
            AppendCachedArtifact(artifact);
            added = true;
        }

        var maxContextFiles = Math.Clamp(limits.Value.MaxFilesPerSubmission, 0, 100);
        var candidates = AssignmentContextCandidateRanking.Select(
            contents,
            section,
            assignmentModule,
            Math.Max(1, maxContextFiles),
            batch.IncludeCourseMaterials);
        var contextFilesAdded = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.File is null)
            {
                if (!string.IsNullOrWhiteSpace(candidate.Module.Description))
                {
                    var descriptionArtifact = new GradingArtifact(
                        Guid.NewGuid(),
                        item.Id,
                        "assignment_context",
                        candidate.Module.Name,
                        "text/html",
                        Sha256: null,
                        SizeBytes: candidate.Module.Description.Length,
                        ExtractionStatus.Succeeded,
                        candidate.Module.Description,
                        BuildContextSummary(candidate),
                        DateTimeOffset.UtcNow);
                    await repository.AddArtifactAsync(descriptionArtifact, cancellationToken);
                    AppendCachedArtifact(descriptionArtifact);
                    added = true;
                }

                continue;
            }

            if (contextFilesAdded >= maxContextFiles)
            {
                break;
            }

            var candidateArtifact = BuildPendingArtifact(
                item.Id,
                "assignment_context",
                string.IsNullOrWhiteSpace(candidate.File.FileName) ? "context-file" : candidate.File.FileName,
                candidate.File.MimeType,
                candidate.File.FileSize,
                candidate.File.FileUrl,
                BuildContextSummary(candidate));
            await repository.AddArtifactAsync(candidateArtifact, cancellationToken);
            AppendCachedArtifact(candidateArtifact);
            added = true;
            contextFilesAdded++;
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
            AppendCachedArtifact(updated);
        }
        else
        {
            await repository.UpdateArtifactAsync(updated, cancellationToken);
            ReplaceCachedArtifact(updated);
        }
    }

    private async Task<IReadOnlyList<GradingArtifact>> GetExistingArtifactsAsync(
        Guid gradingItemId,
        CancellationToken cancellationToken)
    {
        if (artifactsCache.TryGetValue(gradingItemId, out var cached))
        {
            return cached;
        }

        var loaded = await repository.ListArtifactsByItemAsync(gradingItemId, cancellationToken);
        artifactsCache[gradingItemId] = loaded;
        return loaded;
    }

    private void AppendCachedArtifact(GradingArtifact artifact)
    {
        if (artifactsCache.TryGetValue(artifact.GradingItemId, out var cached))
        {
            artifactsCache[artifact.GradingItemId] = cached.Concat([artifact]).ToArray();
        }
    }

    private void ReplaceCachedArtifact(GradingArtifact artifact)
    {
        if (artifactsCache.TryGetValue(artifact.GradingItemId, out var cached))
        {
            artifactsCache[artifact.GradingItemId] = cached
                .Where(existing => existing.Id != artifact.Id)
                .Concat([artifact])
                .ToArray();
        }
    }

    private static GradingArtifact BuildPendingArtifact(
        Guid gradingItemId,
        string artifactType,
        string? filename,
        string? mimeType,
        long? sizeBytes,
        string? sourceUrl,
        string? summaryRef = null)
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
            SummaryRef: normalizedSource is null ? "source_url_invalid" : summaryRef ?? "pending_resource",
            DateTimeOffset.UtcNow,
            normalizedSource);
    }

    private static string BuildContextSummary(AssignmentContextCandidateSelection candidate) =>
        $"section:{candidate.Section.SectionNumber};distance:{candidate.DistanceFromAssignment};" +
        $"score:{candidate.Score.ToString("0.##", CultureInfo.InvariantCulture)};reason:{candidate.Reason}";

    private static bool IsAssignmentModule(CourseModuleSummary module, string assignmentId) =>
        string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(module.InstanceId, assignmentId, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(module.ModuleId, assignmentId, StringComparison.OrdinalIgnoreCase));

}
