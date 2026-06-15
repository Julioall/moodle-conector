using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

public sealed record CreateAssistedGradingBatchCommand(
    string UserExternalId,
    string CourseId,
    IReadOnlyList<string> AssignmentIds,
    IReadOnlyList<string> SubmissionIds,
    int MaxItems,
    bool OnlyAwaitingGrading,
    bool IncludeRubric = true,
    bool IncludeSubmissionFiles = true,
    bool IncludeCourseMaterials = false,
    string? TeacherInstructions = null,
    string Priority = "normal") : IRequest<CreateAssistedGradingBatchResult>;

public sealed record CreateAssistedGradingBatchResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentIds")] IReadOnlyList<string> AssignmentIds,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("acceptedItems")] int AcceptedItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public sealed record GetAssistedGradingBatchStatusQuery(
    Guid BatchJobId,
    int Page,
    int PageSize) : IRequest<AssistedGradingBatchStatusResult>;

public sealed record CancelAssistedGradingBatchCommand(
    Guid BatchJobId) : IRequest<CancelAssistedGradingBatchResult>;

public sealed record CancelAssistedGradingBatchResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message);

public sealed record AssistedGradingBatchStatusResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("processedItems")] int ProcessedItems,
    [property: JsonPropertyName("readyItems")] int ReadyItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("hasMore")] bool HasMore,
    [property: JsonPropertyName("items")] IReadOnlyList<AssistedGradingBatchStatusItem> Items,
    [property: JsonPropertyName("nextReadyItems")] IReadOnlyList<AssistedGradingBatchStatusItem> NextReadyItems,
    [property: JsonPropertyName("errorsByCategory")] IReadOnlyDictionary<string, int> ErrorsByCategory,
    [property: JsonPropertyName("processingMetrics")] GradingBatchProcessingMetrics ProcessingMetrics);

public sealed record GradingBatchProcessingMetrics(
    [property: JsonPropertyName("progressPercent")] int ProgressPercent,
    [property: JsonPropertyName("readyPercent")] int ReadyPercent,
    [property: JsonPropertyName("blockedPercent")] int BlockedPercent,
    [property: JsonPropertyName("failedPercent")] int FailedPercent,
    [property: JsonPropertyName("pendingItems")] int PendingItems,
    [property: JsonPropertyName("canLaunch")] bool CanLaunch);

public sealed record AssistedGradingBatchStatusItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("commitStatus")] string CommitStatus);

public sealed record GetAssistedGradingCoordinationReportQuery(
    Guid BatchJobId) : IRequest<AssistedGradingCoordinationReportResult>;

public sealed record AssistedGradingCoordinationReportResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentIds")] IReadOnlyList<string> AssignmentIds,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("processedItems")] int ProcessedItems,
    [property: JsonPropertyName("readyItems")] int ReadyItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("reviewedItems")] int ReviewedItems,
    [property: JsonPropertyName("pendingReviewItems")] int PendingReviewItems,
    [property: JsonPropertyName("committedItems")] int CommittedItems,
    [property: JsonPropertyName("launchPendingItems")] int LaunchPendingItems,
    [property: JsonPropertyName("lowConfidenceItems")] int LowConfidenceItems,
    [property: JsonPropertyName("averageConfidence")] decimal? AverageConfidence,
    [property: JsonPropertyName("averageSuggestedGrade")] decimal? AverageSuggestedGrade,
    [property: JsonPropertyName("averageFinalGrade")] decimal? AverageFinalGrade,
    [property: JsonPropertyName("statusCounts")] IReadOnlyDictionary<string, int> StatusCounts,
    [property: JsonPropertyName("reviewStatusCounts")] IReadOnlyDictionary<string, int> ReviewStatusCounts,
    [property: JsonPropertyName("commitStatusCounts")] IReadOnlyDictionary<string, int> CommitStatusCounts,
    [property: JsonPropertyName("attentionItems")] IReadOnlyList<AssistedGradingCoordinationAttentionItem> AttentionItems,
    [property: JsonPropertyName("criteriaNeedingReview")] IReadOnlyList<AssistedGradingCoordinationCriterionSummary> CriteriaNeedingReview,
    [property: JsonPropertyName("reportMarkdown")] string ReportMarkdown);

public sealed record AssistedGradingCoordinationAttentionItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("commitStatus")] string CommitStatus,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("suggestedGrade")] decimal? SuggestedGrade,
    [property: JsonPropertyName("finalGrade")] decimal? FinalGrade,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record AssistedGradingCoordinationCriterionSummary(
    [property: JsonPropertyName("criterionId")] string? CriterionId,
    [property: JsonPropertyName("criterionText")] string CriterionText,
    [property: JsonPropertyName("itemCount")] int ItemCount,
    [property: JsonPropertyName("teacherReviewRequiredItems")] int TeacherReviewRequiredItems,
    [property: JsonPropertyName("itemsWithGaps")] int ItemsWithGaps,
    [property: JsonPropertyName("averageSuggestedPoints")] decimal? AverageSuggestedPoints,
    [property: JsonPropertyName("averageMaxPoints")] decimal? AverageMaxPoints);

public sealed record GetAssistedGradingItemQuery(
    Guid GradingItemId,
    Guid? BatchJobId = null) : IRequest<AssistedGradingItemDetailResult>;

public sealed record AssistedGradingItemDetailResult(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("attemptNumber")] int? AttemptNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("suggestedGrade")] decimal? SuggestedGrade,
    [property: JsonPropertyName("finalGrade")] decimal? FinalGrade,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("draftFeedback")] string? DraftFeedback,
    [property: JsonPropertyName("finalFeedback")] string? FinalFeedback,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("commitStatus")] string CommitStatus,
    [property: JsonPropertyName("teacherDecision")] string? TeacherDecision,
    [property: JsonPropertyName("reviewNotes")] string? ReviewNotes,
    [property: JsonPropertyName("draftVersionHash")] string DraftVersionHash,
    [property: JsonPropertyName("pendingIssues")] IReadOnlyList<string> PendingIssues,
    [property: JsonPropertyName("evidence")] IReadOnlyList<AssistedGradingEvidenceResult> Evidence,
    [property: JsonPropertyName("privateNotesToTeacher")] string? PrivateNotesToTeacher = null);

public sealed record AssistedGradingEvidenceResult(
    [property: JsonPropertyName("criterionId")] string? CriterionId,
    [property: JsonPropertyName("criterionText")] string CriterionText,
    [property: JsonPropertyName("maxPoints")] decimal? MaxPoints,
    [property: JsonPropertyName("suggestedPoints")] decimal? SuggestedPoints,
    [property: JsonPropertyName("evidenceText")] string? EvidenceText,
    [property: JsonPropertyName("gapsText")] string? GapsText,
    [property: JsonPropertyName("teacherReviewRequired")] bool TeacherReviewRequired);

public sealed record UpdateAssistedGradingDraftCommand(
    Guid GradingItemId,
    decimal? FinalGrade,
    string FinalFeedback,
    string TeacherDecision,
    string? ReviewNotes,
    string ExpectedReviewStatus) : IRequest<AssistedGradingItemDetailResult>;

public sealed class CreateAssistedGradingBatchCommandHandler(
    IGradingReviewRepository repository,
    IMediator mediator,
    ICurrentUserContext currentUser,
    IMoodleUserResolver moodleUserResolver,
    IMoodleAuditLogRepository auditLogs,
    IGradingBatchOrchestrator orchestrator,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleSubmissionFileGateway fileGateway,
    IDocumentExtractionService extractionService,
    IOptions<GradingLimitsOptions>? limits = null)
    : IRequestHandler<CreateAssistedGradingBatchCommand, CreateAssistedGradingBatchResult>
{
    private readonly GradingLimitsOptions _limits = limits?.Value ?? new GradingLimitsOptions();

    public async Task<CreateAssistedGradingBatchResult> Handle(
        CreateAssistedGradingBatchCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", nameof(request.UserExternalId));
        }

        if (string.IsNullOrWhiteSpace(request.CourseId))
        {
            throw new ArgumentException("O curso e obrigatorio.", nameof(request.CourseId));
        }

        var assignmentIds = request.AssignmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (assignmentIds.Length == 0)
        {
            throw new ArgumentException("Informe pelo menos uma tarefa.", nameof(request.AssignmentIds));
        }

        var safeMaxItems = Math.Clamp(request.MaxItems, 1, 400);
        var selectedSubmissionIds = request.SubmissionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedItems = new List<AssistedGradingItemSeed>();
        string? resolvedCourseId = null;
        var warnings = new List<string>();

        foreach (var assignmentId in assignmentIds)
        {
            var page = 1;
            while (selectedItems.Count < safeMaxItems)
            {
                var remaining = safeMaxItems - selectedItems.Count;
                var submissionsPage = await mediator.Send(
                    new ListAssignmentSubmissionsQuery(
                        request.UserExternalId,
                        request.CourseId,
                        assignmentId,
                        request.OnlyAwaitingGrading ? AssignmentSubmissionFilter.NeedsGrading : AssignmentSubmissionFilter.All,
                        page,
                        Math.Min(remaining, 100),
                        Since: null,
                        Before: null,
                        IncludeLate: true,
                        IncludeUngraded: true),
                    cancellationToken);

                if (submissionsPage is null)
                {
                    warnings.Add($"Tarefa {assignmentId} nao encontrada para o usuario atual.");
                    break;
                }

                resolvedCourseId ??= submissionsPage.CourseId;
                foreach (var submission in submissionsPage.Submissions)
                {
                    if (selectedSubmissionIds.Count > 0 &&
                        (submission.SubmissionId is null || !selectedSubmissionIds.Contains(submission.SubmissionId)))
                    {
                        continue;
                    }

                    selectedItems.Add(new AssistedGradingItemSeed(
                        submissionsPage.CourseId,
                        submissionsPage.AssignmentId,
                        submission.SubmissionId,
                        submission.UserId,
                        submission.AttemptNumber,
                        submission.Files ?? []));
                    if (selectedItems.Count >= safeMaxItems)
                    {
                        break;
                    }
                }

                if (!submissionsPage.HasMore)
                {
                    break;
                }

                page++;
            }
        }

        var courseId = ParsePositiveLong(resolvedCourseId ?? request.CourseId, "courseId");
        var assignmentIdsAsLong = selectedItems.Count > 0
            ? selectedItems.Select(item => ParsePositiveLong(item.AssignmentId, "assignmentId")).Distinct().ToArray()
            : assignmentIds.Select(id => ParsePositiveLong(id, "assignmentId")).Distinct().ToArray();
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        var batch = AssistedGradingBatch.Create(
            courseId,
            assignmentIdsAsLong,
            currentUser.Subject,
            moodleUserId,
            selectedItems.Count);

        await repository.AddBatchAsync(batch, cancellationToken);
        var assignmentContextCache = new Dictionary<AssignmentContextCacheKey, IReadOnlyList<ContextArtifactTemplate>>();
        foreach (var seed in selectedItems)
        {
            var item = AssistedGradingItem.Create(
                batch.Id,
                ParsePositiveLong(seed.CourseId, "courseId"),
                ParsePositiveLong(seed.AssignmentId, "assignmentId"),
                ParseNullablePositiveLong(seed.SubmissionId, "submissionId"),
                ParsePositiveLong(seed.StudentId, "studentId"),
                seed.AttemptNumber);

            await repository.AddItemAsync(item, cancellationToken);
            if (request.IncludeSubmissionFiles)
            {
                await AddSubmissionFileArtifactsAsync(
                    request.UserExternalId,
                    item.Id,
                    seed.Files,
                    warnings,
                    cancellationToken);
            }

            if (request.IncludeRubric || request.IncludeCourseMaterials)
            {
                await AddAssignmentContextArtifactsAsync(
                    request.UserExternalId,
                    item,
                    assignmentContextCache,
                    warnings,
                    cancellationToken);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);

        if (selectedItems.Count == safeMaxItems)
        {
            warnings.Add($"Lote limitado aos primeiros {safeMaxItems} item(ns).");
        }

        var createResult = new CreateAssistedGradingBatchResult(
            batch.Id,
            batch.CourseId.ToString(CultureInfo.InvariantCulture),
            batch.AssignmentIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(),
            batch.TotalItems,
            selectedItems.Count,
            BlockedItems: 0,
            batch.Status.ToString(),
            warnings);

        await auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = $"grading-batch-{batch.Id:N}",
            BatchJobId = batch.Id,
            ToolName = "criar_lote_correcao_assistida",
            RiskLevel = ToolRiskLevel.HumanConfirmedWrite,
            ActorSubject = currentUser.Subject,
            ActorEmail = currentUser.Email,
            ActorMoodleUserId = moodleUserId,
            CourseId = batch.CourseId,
            MoodleFunction = "mod_assign_get_submissions",
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                request.CourseId,
                request.AssignmentIds,
                request.SubmissionIds,
                request.MaxItems,
                request.OnlyAwaitingGrading
            }),
            ResponseSummaryJson = AuditPayloadSanitizer.SerializeSanitized(createResult),
            Status = "batch_created"
        }, cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        await orchestrator.EnqueueAsync(batch.Id, cancellationToken);

        return createResult;
    }

    private static long ParsePositiveLong(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico positivo.", parameterName);
    }

    private static long? ParseNullablePositiveLong(string? value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value) ? null : ParsePositiveLong(value, parameterName);
    }

    private async Task AddSubmissionFileArtifactsAsync(
        string userExternalId,
        Guid gradingItemId,
        IReadOnlyList<AssignmentSubmissionFile> files,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var maxFiles = Math.Clamp(_limits.MaxFilesPerSubmission, 0, 100);
        var maxBytes = Math.Max(1, _limits.MaxFileSizeMb) * 1024L * 1024L;

        foreach (var file in files.Take(maxFiles))
        {
            try
            {
                var download = await fileGateway.DownloadFileAsync(
                    userExternalId,
                    file.FileUrl,
                    file.Filename,
                    maxBytes,
                    cancellationToken);
                var extraction = await extractionService.ExtractAsync(
                    download.Filename,
                    download.MimeType,
                    download.Content,
                    cancellationToken);

                await repository.AddArtifactAsync(
                    new GradingArtifact(
                        Guid.NewGuid(),
                        gradingItemId,
                        "submission_file",
                        download.Filename,
                        download.MimeType,
                        download.Sha256Hex,
                        download.SizeBytes,
                        extraction.ExtractionStatus,
                        extraction.ExtractedText,
                        extraction.ErrorMessage,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Arquivo {file.Filename} nao foi extraido: {ex.Message}");
                await repository.AddArtifactAsync(
                    new GradingArtifact(
                        Id: Guid.NewGuid(),
                        GradingItemId: gradingItemId,
                        ArtifactType: "submission_file",
                        Filename: file.Filename,
                        MimeType: file.MimeType,
                        Sha256: null,
                        SizeBytes: file.SizeBytes,
                        ExtractionStatus: ExtractionStatus.Failed,
                        ExtractedTextRef: null,
                        SummaryRef: ex.Message,
                        CreatedAt: DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }
    }

    private async Task AddAssignmentContextArtifactsAsync(
        string userExternalId,
        AssistedGradingItem item,
        Dictionary<AssignmentContextCacheKey, IReadOnlyList<ContextArtifactTemplate>> assignmentContextCache,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var cacheKey = new AssignmentContextCacheKey(item.CourseId, item.AssignmentId);
        if (!assignmentContextCache.TryGetValue(cacheKey, out var templates))
        {
            templates = await BuildAssignmentContextTemplatesAsync(
                userExternalId,
                item,
                warnings,
                cancellationToken);
            assignmentContextCache[cacheKey] = templates;
        }

        foreach (var template in templates)
        {
            await repository.AddArtifactAsync(template.ToArtifact(item.Id), cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ContextArtifactTemplate>> BuildAssignmentContextTemplatesAsync(
        string userExternalId,
        AssistedGradingItem item,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        CourseContentsSummary contents;
        try
        {
            contents = await contentsGateway.GetCourseContentsAsync(
                userExternalId,
                item.CourseId.ToString(CultureInfo.InvariantCulture),
                moduleTypes: [],
                includeHidden: true,
                onlyWithFiles: false,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add($"Nao foi possivel escanear materiais do curso para contexto da tarefa {item.AssignmentId}: {ex.Message}");
            return [];
        }

        var assignmentId = item.AssignmentId.ToString(CultureInfo.InvariantCulture);
        var section = contents.Sections.FirstOrDefault(candidate =>
            candidate.Modules.Any(module => IsAssignmentModule(module, assignmentId)));
        var assignmentModule = section?.Modules.FirstOrDefault(module => IsAssignmentModule(module, assignmentId));
        if (section is null || assignmentModule is null)
        {
            return [];
        }

        var templates = new List<ContextArtifactTemplate>();
        if (!string.IsNullOrWhiteSpace(assignmentModule.Description))
        {
            templates.Add(new ContextArtifactTemplate(
                "assignment_context",
                assignmentModule.Name,
                "text/html",
                Sha256: null,
                SizeBytes: assignmentModule.Description.Length,
                ExtractionStatus.Succeeded,
                assignmentModule.Description,
                SummaryRef: "assignment_description"));
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
            .Take(Math.Max(1, _limits.MaxFilesPerSubmission))
            .ToArray();

        foreach (var entry in nearbyModules)
        {
            if (!string.IsNullOrWhiteSpace(entry.Module.Description))
            {
                templates.Add(new ContextArtifactTemplate(
                    "assignment_context",
                    entry.Module.Name,
                    "text/html",
                    Sha256: null,
                    SizeBytes: entry.Module.Description.Length,
                    ExtractionStatus.Succeeded,
                    entry.Module.Description,
                    SummaryRef: $"section:{section.SectionNumber};distance:{entry.Distance}"));
            }

            foreach (var file in entry.Module.Files.Where(file => !string.IsNullOrWhiteSpace(file.FileUrl)))
            {
                var template = await BuildContextFileArtifactTemplateAsync(
                    userExternalId,
                    file,
                    warnings,
                    cancellationToken);
                if (template is not null)
                {
                    templates.Add(template);
                }
            }
        }

        return templates;
    }

    private async Task<ContextArtifactTemplate?> BuildContextFileArtifactTemplateAsync(
        string userExternalId,
        CourseModuleFile file,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var filename = string.IsNullOrWhiteSpace(file.FileName)
            ? "context-file"
            : file.FileName;
        var maxBytes = Math.Max(1, _limits.MaxFileSizeMb) * 1024L * 1024L;

        try
        {
            var download = await fileGateway.DownloadFileAsync(
                userExternalId,
                file.FileUrl!,
                filename,
                maxBytes,
                cancellationToken);
            var extraction = await extractionService.ExtractAsync(
                download.Filename,
                download.MimeType,
                download.Content,
                cancellationToken);

            return new ContextArtifactTemplate(
                "assignment_context",
                download.Filename,
                download.MimeType,
                download.Sha256Hex,
                download.SizeBytes,
                extraction.ExtractionStatus,
                extraction.ExtractedText,
                extraction.ErrorMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add($"Material de contexto {filename} nao foi extraido: {ex.Message}");
            return null;
        }
    }

    private static bool IsAssignmentModule(CourseModuleSummary module, string assignmentId)
    {
        return string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(module.InstanceId, assignmentId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(module.ModuleId, assignmentId, StringComparison.OrdinalIgnoreCase));
    }

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

    private sealed record AssistedGradingItemSeed(
        string CourseId,
        string AssignmentId,
        string? SubmissionId,
        string StudentId,
        int? AttemptNumber,
        IReadOnlyList<AssignmentSubmissionFile> Files);

    private sealed record AssignmentContextCacheKey(long CourseId, long AssignmentId);

    private sealed record ContextArtifactTemplate(
        string ArtifactType,
        string? Filename,
        string? MimeType,
        string? Sha256,
        long? SizeBytes,
        string ExtractionStatus,
        string? ExtractedTextRef,
        string? SummaryRef)
    {
        public GradingArtifact ToArtifact(Guid gradingItemId)
        {
            return new GradingArtifact(
                Guid.NewGuid(),
                gradingItemId,
                ArtifactType,
                Filename,
                MimeType,
                Sha256,
                SizeBytes,
                ExtractionStatus,
                ExtractedTextRef,
                SummaryRef,
                DateTimeOffset.UtcNow);
        }
    }
}

public sealed class GetAssistedGradingItemQueryHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser)
    : IRequestHandler<GetAssistedGradingItemQuery, AssistedGradingItemDetailResult>
{
    public async Task<AssistedGradingItemDetailResult> Handle(
        GetAssistedGradingItemQuery request,
        CancellationToken cancellationToken)
    {
        if (request.GradingItemId == Guid.Empty)
        {
            throw new ArgumentException("O item de correcao e obrigatorio.", nameof(request.GradingItemId));
        }

        var item = await repository.GetItemAsync(request.GradingItemId, cancellationToken)
            ?? throw new InvalidOperationException("Item de correcao nao encontrado.");
        var batch = await repository.GetBatchAsync(item.BatchId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        if (request.BatchJobId is Guid batchJobId && batchJobId != Guid.Empty && item.BatchId != batchJobId)
        {
            throw new InvalidOperationException("O item informado nao pertence ao lote solicitado.");
        }

        var evidence = await repository.ListEvidenceByItemAsync(item.Id, cancellationToken);

        return new AssistedGradingItemDetailResult(
            item.Id,
            item.BatchId,
            item.CourseId.ToString(CultureInfo.InvariantCulture),
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.AttemptNumber,
            item.Status.ToString(),
            item.SuggestedGrade,
            item.FinalGrade,
            item.Confidence,
            item.DraftFeedback,
            item.FinalFeedback,
            item.ReviewStatus.ToString(),
            item.CommitStatus.ToString(),
            item.TeacherDecision,
            item.ReviewNotes,
            GradingDraftVersionHash.Compute(item),
            BuildPendingIssues(item),
            evidence.Select(ToEvidenceResult).ToArray(),
            item.PrivateNotesToTeacher);
    }

    private static IReadOnlyList<string> BuildPendingIssues(AssistedGradingItem item)
    {
        var pendingIssues = new List<string>();

        if (item.ReviewStatus == GradingReviewStatus.NotReviewed)
        {
            pendingIssues.Add("Revisao humana pendente.");
        }

        if (item.Status == GradingItemStatus.DraftReady && item.Confidence is < 0.5m)
        {
            pendingIssues.Add("Baixa confianca do rascunho assistido; revise criterios, evidencias e nota sugerida antes de aprovar.");
        }

        if (string.IsNullOrWhiteSpace(item.FinalFeedback))
        {
            pendingIssues.Add("Feedback final pendente.");
        }

        if (item.CommitStatus == GradingCommitStatus.Failed && !string.IsNullOrWhiteSpace(item.CommitError))
        {
            pendingIssues.Add($"Falha no lancamento Moodle: {item.CommitError}");
        }

        if (item.Status == GradingItemStatus.Failed &&
            item.CommitStatus != GradingCommitStatus.Failed &&
            !string.IsNullOrWhiteSpace(item.DraftFeedback))
        {
            pendingIssues.Add($"Falha no processamento da correcao assistida: {item.DraftFeedback}");
        }

        return pendingIssues;
    }

    private static AssistedGradingEvidenceResult ToEvidenceResult(GradingEvidence evidence)
    {
        return new AssistedGradingEvidenceResult(
            evidence.CriterionId,
            evidence.CriterionText,
            evidence.MaxPoints,
            evidence.SuggestedPoints,
            evidence.EvidenceText,
            evidence.GapsText,
            evidence.TeacherReviewRequired);
    }
}

public sealed class UpdateAssistedGradingDraftCommandHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser,
    IMoodleUserResolver moodleUserResolver,
    IMoodleAuditLogRepository auditLogs)
    : IRequestHandler<UpdateAssistedGradingDraftCommand, AssistedGradingItemDetailResult>
{
    public async Task<AssistedGradingItemDetailResult> Handle(
        UpdateAssistedGradingDraftCommand request,
        CancellationToken cancellationToken)
    {
        if (request.GradingItemId == Guid.Empty)
        {
            throw new ArgumentException("O item de correcao e obrigatorio.", nameof(request.GradingItemId));
        }

        var item = await repository.GetItemAsync(request.GradingItemId, cancellationToken)
            ?? throw new InvalidOperationException("Item de correcao nao encontrado.");
        var batch = await repository.GetBatchAsync(item.BatchId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        if (!string.Equals(item.ReviewStatus.ToString(), request.ExpectedReviewStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (MatchesExistingReview(item, request))
            {
                return await ToDetailResultAsync(item, cancellationToken);
            }

            throw new InvalidOperationException("O rascunho foi alterado desde a ultima leitura. Consulte o item novamente antes de sobrescrever.");
        }

        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        item.ApplyTeacherReview(
            request.FinalGrade,
            request.FinalFeedback,
            currentUser.Subject,
            moodleUserId,
            request.TeacherDecision,
            request.ReviewNotes);

        var artifacts = await repository.ListArtifactsByItemAsync(item.Id, cancellationToken);
        var fileHashes = artifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.Sha256))
            .Select(artifact => artifact.Sha256!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(hash => hash, StringComparer.Ordinal)
            .ToArray();
        var draftVersionHash = GradingDraftVersionHash.Compute(item);

        await auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = $"grading-batch-{item.BatchId:N}",
            BatchJobId = item.BatchId,
            ToolName = "atualizar_rascunho_correcao",
            RiskLevel = ToolRiskLevel.HumanConfirmedWrite,
            ActorSubject = currentUser.Subject,
            ActorEmail = currentUser.Email,
            ActorMoodleUserId = moodleUserId,
            CourseId = item.CourseId,
            MoodleFunction = null,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                gradingItemId = item.Id,
                batchJobId = item.BatchId,
                request.FinalGrade,
                request.TeacherDecision,
                request.ReviewNotes,
                request.ExpectedReviewStatus,
                draftVersionHash,
                fileHashes
            }),
            ResponseSummaryJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                reviewStatus = item.ReviewStatus.ToString(),
                commitStatus = item.CommitStatus.ToString(),
                reviewedAt = item.ReviewedAt,
                finalGrade = item.FinalGrade
            }),
            Status = "draft_reviewed"
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        return await ToDetailResultAsync(item, cancellationToken);
    }

    private static bool MatchesExistingReview(
        AssistedGradingItem item,
        UpdateAssistedGradingDraftCommand request)
    {
        return item.FinalGrade == request.FinalGrade &&
            string.Equals(item.FinalFeedback, request.FinalFeedback.Trim(), StringComparison.Ordinal) &&
            string.Equals(item.TeacherDecision, Normalize(request.TeacherDecision), StringComparison.Ordinal) &&
            string.Equals(item.ReviewNotes, Normalize(request.ReviewNotes), StringComparison.Ordinal);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<AssistedGradingItemDetailResult> ToDetailResultAsync(
        AssistedGradingItem item,
        CancellationToken cancellationToken)
    {
        var evidence = await repository.ListEvidenceByItemAsync(item.Id, cancellationToken);
        var draftVersionHash = GradingDraftVersionHash.Compute(item);
        var pendingIssues = BuildPendingIssues(item);

        return new AssistedGradingItemDetailResult(
            item.Id,
            item.BatchId,
            item.CourseId.ToString(CultureInfo.InvariantCulture),
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.AttemptNumber,
            item.Status.ToString(),
            item.SuggestedGrade,
            item.FinalGrade,
            item.Confidence,
            item.DraftFeedback,
            item.FinalFeedback,
            item.ReviewStatus.ToString(),
            item.CommitStatus.ToString(),
            item.TeacherDecision,
            item.ReviewNotes,
            draftVersionHash,
            pendingIssues,
            evidence.Select(ToEvidenceResult).ToArray(),
            item.PrivateNotesToTeacher);
    }

    private static IReadOnlyList<string> BuildPendingIssues(AssistedGradingItem item)
    {
        var pendingIssues = new List<string>();

        if (item.ReviewStatus == GradingReviewStatus.NotReviewed)
        {
            pendingIssues.Add("Revisao humana pendente.");
        }

        if (item.Status == GradingItemStatus.DraftReady && item.Confidence is < 0.5m)
        {
            pendingIssues.Add("Baixa confianca do rascunho assistido; revise criterios, evidencias e nota sugerida antes de aprovar.");
        }

        if (string.IsNullOrWhiteSpace(item.FinalFeedback))
        {
            pendingIssues.Add("Feedback final pendente.");
        }

        if (item.CommitStatus == GradingCommitStatus.Failed && !string.IsNullOrWhiteSpace(item.CommitError))
        {
            pendingIssues.Add($"Falha no lancamento Moodle: {item.CommitError}");
        }

        if (item.Status == GradingItemStatus.Failed &&
            item.CommitStatus != GradingCommitStatus.Failed &&
            !string.IsNullOrWhiteSpace(item.DraftFeedback))
        {
            pendingIssues.Add($"Falha no processamento da correcao assistida: {item.DraftFeedback}");
        }

        return pendingIssues;
    }

    private static AssistedGradingEvidenceResult ToEvidenceResult(GradingEvidence evidence)
    {
        return new AssistedGradingEvidenceResult(
            evidence.CriterionId,
            evidence.CriterionText,
            evidence.MaxPoints,
            evidence.SuggestedPoints,
            evidence.EvidenceText,
            evidence.GapsText,
            evidence.TeacherReviewRequired);
    }
}

public sealed class GetAssistedGradingBatchStatusQueryHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser)
    : IRequestHandler<GetAssistedGradingBatchStatusQuery, AssistedGradingBatchStatusResult>
{
    public async Task<AssistedGradingBatchStatusResult> Handle(
        GetAssistedGradingBatchStatusQuery request,
        CancellationToken cancellationToken)
    {
        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var items = await repository.ListItemsByBatchAsync(batch.Id, page, pageSize + 1, cancellationToken);
        var totalItems = await repository.CountItemsByBatchAsync(batch.Id, cancellationToken);

        var nextReady = items
            .Where(item => item.Status == GradingItemStatus.DraftReady && item.ReviewStatus == GradingReviewStatus.NotReviewed)
            .Take(5)
            .Select(ToStatusItem)
            .ToArray();

        var errorsByCategory = new Dictionary<string, int>();
        foreach (var item in items.Where(item => item.Status == GradingItemStatus.Failed || item.Status == GradingItemStatus.Blocked))
        {
            var category = item.Status == GradingItemStatus.Blocked ? "blocked" : "failed";
            errorsByCategory.TryGetValue(category, out var current);
            errorsByCategory[category] = current + 1;
        }

        var metrics = BuildMetrics(batch);

        return new AssistedGradingBatchStatusResult(
            batch.Id,
            batch.Status.ToString(),
            batch.TotalItems,
            batch.ProcessedItems,
            batch.ReadyItems,
            batch.BlockedItems,
            batch.FailedItems,
            page,
            pageSize,
            HasMore: items.Count > pageSize || page * pageSize < totalItems,
            items.Take(pageSize).Select(ToStatusItem).ToArray(),
            NextReadyItems: nextReady,
            ErrorsByCategory: errorsByCategory,
            ProcessingMetrics: metrics);
    }

    private static AssistedGradingBatchStatusItem ToStatusItem(AssistedGradingItem item)
    {
        return new AssistedGradingBatchStatusItem(
            item.Id,
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.Status.ToString(),
            item.ReviewStatus.ToString(),
            item.CommitStatus.ToString());
    }

    private static GradingBatchProcessingMetrics BuildMetrics(AssistedGradingBatch batch)
    {
        var total = batch.TotalItems > 0 ? batch.TotalItems : 1;
        var progressPercent = (int)Math.Round((double)batch.ProcessedItems / total * 100);
        var readyPercent = (int)Math.Round((double)batch.ReadyItems / total * 100);
        var blockedPercent = (int)Math.Round((double)batch.BlockedItems / total * 100);
        var failedPercent = (int)Math.Round((double)batch.FailedItems / total * 100);
        var pendingItems = Math.Max(0, batch.TotalItems - batch.ProcessedItems - batch.BlockedItems - batch.FailedItems);
        var canLaunch = batch.ReadyItems > 0 &&
            batch.Status is GradingBatchStatus.ReadyForReview or GradingBatchStatus.Processing;

        return new GradingBatchProcessingMetrics(
            progressPercent,
            readyPercent,
            blockedPercent,
            failedPercent,
            pendingItems,
            canLaunch);
    }
}

public sealed class GetAssistedGradingCoordinationReportQueryHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser)
    : IRequestHandler<GetAssistedGradingCoordinationReportQuery, AssistedGradingCoordinationReportResult>
{
    private const int PageSize = 100;
    private const int MaxAttentionItems = 25;
    private const int MaxCriteriaSummaries = 10;

    public async Task<AssistedGradingCoordinationReportResult> Handle(
        GetAssistedGradingCoordinationReportQuery request,
        CancellationToken cancellationToken)
    {
        if (request.BatchJobId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(request.BatchJobId));
        }

        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        var items = await LoadAllItemsAsync(batch.Id, cancellationToken);
        var evidenceByItem = new Dictionary<Guid, IReadOnlyList<GradingEvidence>>();
        foreach (var item in items)
        {
            evidenceByItem[item.Id] = await repository.ListEvidenceByItemAsync(item.Id, cancellationToken);
        }

        var statusCounts = CountBy(items, item => item.Status.ToString());
        var reviewStatusCounts = CountBy(items, item => item.ReviewStatus.ToString());
        var commitStatusCounts = CountBy(items, item => item.CommitStatus.ToString());
        var attentionItems = BuildAttentionItems(items, evidenceByItem);
        var criteriaNeedingReview = BuildCriterionSummaries(evidenceByItem.Values.SelectMany(evidence => evidence));
        var reviewedItems = items.Count(item => item.ReviewStatus == GradingReviewStatus.Reviewed);
        var pendingReviewItems = items.Count(item =>
            item.ReviewStatus != GradingReviewStatus.Reviewed &&
            item.Status is GradingItemStatus.DraftReady or GradingItemStatus.ReadyToCommit);
        var committedItems = items.Count(item =>
            item.Status == GradingItemStatus.Committed ||
            item.CommitStatus == GradingCommitStatus.Succeeded);
        var launchPendingItems = items.Count(item => item.CommitStatus == GradingCommitStatus.Pending);
        var lowConfidenceItems = items.Count(HasLowConfidence);
        var generatedAt = DateTimeOffset.UtcNow;

        var report = new AssistedGradingCoordinationReportResult(
            batch.Id,
            generatedAt,
            batch.CourseId.ToString(CultureInfo.InvariantCulture),
            batch.AssignmentIds
                .Select(id => id.ToString(CultureInfo.InvariantCulture))
                .ToArray(),
            batch.Status.ToString(),
            batch.TotalItems,
            batch.ProcessedItems,
            batch.ReadyItems,
            batch.BlockedItems,
            batch.FailedItems,
            reviewedItems,
            pendingReviewItems,
            committedItems,
            launchPendingItems,
            lowConfidenceItems,
            AverageOrNull(items.Select(item => item.Confidence)),
            AverageOrNull(items.Select(item => item.SuggestedGrade)),
            AverageOrNull(items.Select(item => item.FinalGrade)),
            statusCounts,
            reviewStatusCounts,
            commitStatusCounts,
            attentionItems,
            criteriaNeedingReview,
            ReportMarkdown: string.Empty);

        return report with { ReportMarkdown = BuildReportMarkdown(report) };
    }

    private async Task<IReadOnlyList<AssistedGradingItem>> LoadAllItemsAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var totalItems = await repository.CountItemsByBatchAsync(batchId, cancellationToken);
        var items = new List<AssistedGradingItem>(totalItems);
        for (var page = 1; items.Count < totalItems; page++)
        {
            var pageItems = await repository.ListItemsByBatchAsync(batchId, page, PageSize, cancellationToken);
            if (pageItems.Count == 0)
            {
                break;
            }

            items.AddRange(pageItems);
        }

        return items;
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IReadOnlyList<AssistedGradingItem> items,
        Func<AssistedGradingItem, string> selector)
    {
        return items
            .GroupBy(selector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    private static IReadOnlyList<AssistedGradingCoordinationAttentionItem> BuildAttentionItems(
        IReadOnlyList<AssistedGradingItem> items,
        IReadOnlyDictionary<Guid, IReadOnlyList<GradingEvidence>> evidenceByItem)
    {
        return items
            .Select(item => BuildAttentionItem(item, evidenceByItem))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.Item.AssignmentId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Item.StudentId, StringComparer.Ordinal)
            .Take(MaxAttentionItems)
            .Select(entry => entry.Item)
            .ToArray();
    }

    private static AttentionEntry? BuildAttentionItem(
        AssistedGradingItem item,
        IReadOnlyDictionary<Guid, IReadOnlyList<GradingEvidence>> evidenceByItem)
    {
        var reasons = new List<string>();
        var priority = 99;
        var evidence = evidenceByItem.TryGetValue(item.Id, out var itemEvidence)
            ? itemEvidence
            : [];

        if (item.CommitStatus == GradingCommitStatus.Failed)
        {
            reasons.Add("Falha no lancamento Moodle: " + Shorten(item.CommitError ?? item.DraftFeedback));
            priority = Math.Min(priority, 0);
        }
        else if (item.Status == GradingItemStatus.Failed)
        {
            reasons.Add("Falha no processamento: " + Shorten(item.DraftFeedback));
            priority = Math.Min(priority, 1);
        }

        if (item.Status == GradingItemStatus.Blocked)
        {
            reasons.Add("Bloqueado: " + Shorten(item.DraftFeedback));
            priority = Math.Min(priority, 2);
        }

        if (HasLowConfidence(item))
        {
            reasons.Add("Baixa confianca do rascunho assistido.");
            priority = Math.Min(priority, 3);
        }

        if (item.Status == GradingItemStatus.DraftReady && item.ReviewStatus != GradingReviewStatus.Reviewed)
        {
            reasons.Add("Revisao humana pendente.");
            priority = Math.Min(priority, 4);
        }

        if (item.CommitStatus == GradingCommitStatus.Pending)
        {
            reasons.Add("Lancamento Moodle pendente de previa/confirmacao.");
            priority = Math.Min(priority, 5);
        }

        if (evidence.Any(entry => entry.TeacherReviewRequired))
        {
            reasons.Add("Ha criterio marcado para revisao humana.");
            priority = Math.Min(priority, 6);
        }

        if (evidence.Any(entry => !string.IsNullOrWhiteSpace(entry.GapsText)))
        {
            reasons.Add("Ha lacunas registradas em criterio.");
            priority = Math.Min(priority, 7);
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        var attentionItem = new AssistedGradingCoordinationAttentionItem(
            item.Id,
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.Status.ToString(),
            item.ReviewStatus.ToString(),
            item.CommitStatus.ToString(),
            item.Confidence,
            item.SuggestedGrade,
            item.FinalGrade,
            string.Join(" ", reasons.Distinct(StringComparer.Ordinal)));

        return new AttentionEntry(priority, attentionItem);
    }

    private static IReadOnlyList<AssistedGradingCoordinationCriterionSummary> BuildCriterionSummaries(
        IEnumerable<GradingEvidence> evidence)
    {
        return evidence
            .GroupBy(evidenceItem =>
                string.IsNullOrWhiteSpace(evidenceItem.CriterionId)
                    ? NormalizeCriterionKey(evidenceItem.CriterionText)
                    : evidenceItem.CriterionId!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var criterionId = entries
                    .Select(entry => entry.CriterionId)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var criterionText = entries
                    .Select(entry => entry.CriterionText)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Criterio sem texto.";
                var teacherReviewRequiredItems = entries
                    .Where(entry => entry.TeacherReviewRequired)
                    .Select(entry => entry.GradingItemId)
                    .Distinct()
                    .Count();
                var itemsWithGaps = entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.GapsText))
                    .Select(entry => entry.GradingItemId)
                    .Distinct()
                    .Count();

                return new AssistedGradingCoordinationCriterionSummary(
                    criterionId,
                    criterionText,
                    entries.Select(entry => entry.GradingItemId).Distinct().Count(),
                    teacherReviewRequiredItems,
                    itemsWithGaps,
                    AverageOrNull(entries.Select(entry => entry.SuggestedPoints)),
                    AverageOrNull(entries.Select(entry => entry.MaxPoints)));
            })
            .Where(summary => summary.TeacherReviewRequiredItems > 0 || summary.ItemsWithGaps > 0)
            .OrderByDescending(summary => summary.TeacherReviewRequiredItems)
            .ThenByDescending(summary => summary.ItemsWithGaps)
            .ThenByDescending(summary => summary.ItemCount)
            .ThenBy(summary => summary.CriterionText, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCriteriaSummaries)
            .ToArray();
    }

    private static bool HasLowConfidence(AssistedGradingItem item)
    {
        return item.Status is GradingItemStatus.DraftReady or GradingItemStatus.ReadyToCommit &&
            item.Confidence is < 0.5m;
    }

    private static decimal? AverageOrNull(IEnumerable<decimal?> values)
    {
        var concreteValues = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return concreteValues.Length == 0
            ? null
            : Math.Round(concreteValues.Average(), 2, MidpointRounding.AwayFromZero);
    }

    private static string BuildReportMarkdown(AssistedGradingCoordinationReportResult report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Relatorio consolidado de correcao assistida");
        builder.AppendLine();
        builder.AppendLine($"- Lote: `{report.BatchJobId}`");
        builder.AppendLine($"- Curso: `{report.CourseId}`");
        builder.AppendLine($"- Tarefas: {string.Join(", ", report.AssignmentIds.Select(id => $"`{id}`"))}");
        builder.AppendLine($"- Status: `{report.Status}`");
        builder.AppendLine($"- Gerado em UTC: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();
        builder.AppendLine("## Resumo");
        builder.AppendLine();
        builder.AppendLine($"- Total: {report.TotalItems}");
        builder.AppendLine($"- Processados: {report.ProcessedItems}");
        builder.AppendLine($"- Prontos para revisao: {report.ReadyItems}");
        builder.AppendLine($"- Revisados: {report.ReviewedItems}");
        builder.AppendLine($"- Revisao pendente: {report.PendingReviewItems}");
        builder.AppendLine($"- Bloqueados: {report.BlockedItems}");
        builder.AppendLine($"- Falhos: {report.FailedItems}");
        builder.AppendLine($"- Lancamento pendente: {report.LaunchPendingItems}");
        builder.AppendLine($"- Lancados no Moodle: {report.CommittedItems}");
        builder.AppendLine($"- Baixa confianca: {report.LowConfidenceItems}");
        builder.AppendLine($"- Confianca media: {FormatDecimal(report.AverageConfidence)}");
        builder.AppendLine($"- Nota sugerida media: {FormatDecimal(report.AverageSuggestedGrade)}");
        builder.AppendLine($"- Nota final media: {FormatDecimal(report.AverageFinalGrade)}");

        builder.AppendLine();
        builder.AppendLine("## Itens que exigem atencao");
        builder.AppendLine();
        if (report.AttentionItems.Count == 0)
        {
            builder.AppendLine("- Nenhum item critico identificado no lote.");
        }
        else
        {
            foreach (var item in report.AttentionItems)
            {
                builder.AppendLine(
                    $"- Estudante `{item.StudentId}`, tarefa `{item.AssignmentId}`, item `{item.GradingItemId}`: {item.Reason}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Criterios com maior necessidade de revisao");
        builder.AppendLine();
        if (report.CriteriaNeedingReview.Count == 0)
        {
            builder.AppendLine("- Nenhum criterio com lacuna ou revisao obrigatoria foi registrado.");
        }
        else
        {
            foreach (var criterion in report.CriteriaNeedingReview)
            {
                builder.AppendLine(
                    $"- {criterion.CriterionText}: {criterion.TeacherReviewRequiredItems} item(ns) exigem revisao, {criterion.ItemsWithGaps} item(ns) com lacunas.");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Shorten(string? value, int maxLength = 180)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "motivo nao informado.";
        }

        var normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static string NormalizeCriterionKey(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "criterio-sem-texto"
            : value.Trim().ToUpperInvariant();
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/d";
    }

    private sealed record AttentionEntry(
        int Priority,
        AssistedGradingCoordinationAttentionItem Item);
}

public sealed class CancelAssistedGradingBatchCommandHandler(
    IGradingBatchOrchestrator orchestrator,
    IGradingReviewRepository repository)
    : IRequestHandler<CancelAssistedGradingBatchCommand, CancelAssistedGradingBatchResult>
{
    public async Task<CancelAssistedGradingBatchResult> Handle(
        CancelAssistedGradingBatchCommand request,
        CancellationToken cancellationToken)
    {
        if (request.BatchJobId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(request.BatchJobId));
        }

        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");

        await orchestrator.CancelAsync(batch.Id, cancellationToken);

        return new CancelAssistedGradingBatchResult(
            batch.Id,
            batch.Status.ToString(),
            "Lote cancelado com sucesso.");
    }
}
