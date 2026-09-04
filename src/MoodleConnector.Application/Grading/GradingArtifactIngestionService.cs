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
                    SummaryRef: normalizedSource is null ? "source_url_invalid" : "pending_resource",
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
