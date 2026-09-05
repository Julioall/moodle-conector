using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
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
    string Priority = "normal",
    IReadOnlyList<AssignmentSubmissionSummary>? PrefetchedSubmissions = null,
    string? IdempotencyKey = null,
    string? CourseDisplayName = null) : IRequest<CreateAssistedGradingBatchResult>;

public sealed record AssistedGradingBatchDiscoveryFailure(
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("auditId")] string AuditId,
    [property: JsonPropertyName("moodleFunction")] string? MoodleFunction,
    [property: JsonPropertyName("durationMs")] long? DurationMs);

public sealed record CreateAssistedGradingBatchResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentIds")] IReadOnlyList<string> AssignmentIds,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("acceptedItems")] int AcceptedItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("discoveryFailures")] IReadOnlyList<AssistedGradingBatchDiscoveryFailure>? DiscoveryFailures = null);

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
    [property: JsonPropertyName("executionUnknownItems")] int ExecutionUnknownItems,
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
    string ExpectedReviewStatus,
    string? ExpectedDraftVersionHash = null) : IRequest<AssistedGradingItemDetailResult>;

public sealed class CreateAssistedGradingBatchCommandHandler(
    IGradingReviewRepository repository,
    IMediator mediator,
    ICurrentUserContext currentUser,
    IMoodleUserResolver moodleUserResolver,
    IMoodleAuditLogRepository auditLogs,
    IGradingBatchOrchestrator orchestrator,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IOptions<GradingLimitsOptions>? limits = null,
    IConnectorExecutionContext? executionContext = null,
    IMoodleConnectionSelection? connectionSelection = null,
    IMoodleConnectorCredentialsProvider? credentialsProvider = null,
    IMoodleAssignmentSettingsGateway? settingsGateway = null,
    IMoodleAssignmentGradeReadGateway? gradeReadGateway = null)
    : IRequestHandler<CreateAssistedGradingBatchCommand, CreateAssistedGradingBatchResult>
{
    private readonly GradingLimitsOptions _limits = limits?.Value ?? new GradingLimitsOptions();

    public async Task<CreateAssistedGradingBatchResult> Handle(
        CreateAssistedGradingBatchCommand request,
        CancellationToken cancellationToken)
    {
        _ = mediator; // Mantém compatibilidade de injeção durante a transição do agregador para a leitura direta.
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

        if (request.PrefetchedSubmissions is not null && assignmentIds.Length != 1)
        {
            throw new ArgumentException(
                "Entregas pre-carregadas exigem exatamente uma tarefa no lote.",
                nameof(request.AssignmentIds));
        }

        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();
        if (normalizedIdempotencyKey is not null)
        {
            var existingBatch = await repository.GetBatchByIdempotencyKeyAsync(
                currentUser.Subject,
                normalizedIdempotencyKey,
                cancellationToken);
            if (existingBatch is not null)
            {
                return new CreateAssistedGradingBatchResult(
                    existingBatch.Id,
                    existingBatch.CourseId.ToString(CultureInfo.InvariantCulture),
                    existingBatch.AssignmentIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(),
                    existingBatch.TotalItems,
                    existingBatch.TotalItems,
                    BlockedItems: existingBatch.BlockedItems,
                    Status: "IdempotentReplay",
                    Warnings: ["Uma solicitacao anterior com a mesma chave ja criou este lote."]);
            }
        }

        var safeMaxItems = Math.Clamp(request.MaxItems, 1, 400);
        var selectedSubmissionIds = request.SubmissionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedItems = new List<AssistedGradingItemSeed>();
        var selectedSubmissionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? resolvedCourseId = null;
        var warnings = new List<string>();
        var discoveryFailures = new List<AssistedGradingBatchDiscoveryFailure>();
        Exception? firstDiscoveryFailure = null;

        if (request.PrefetchedSubmissions is not null)
        {
            foreach (var submission in request.PrefetchedSubmissions)
            {
                if (selectedItems.Count >= safeMaxItems ||
                    (selectedSubmissionIds.Count > 0 &&
                     (submission.SubmissionId is null || !selectedSubmissionIds.Contains(submission.SubmissionId))) ||
                    (request.OnlyAwaitingGrading && !submission.NeedsGrading))
                {
                    continue;
                }

                if (!selectedSubmissionKeys.Add(BuildSubmissionSelectionKey(assignmentIds[0], submission)))
                {
                    continue;
                }

                selectedItems.Add(new AssistedGradingItemSeed(
                    request.CourseId,
                    assignmentIds[0],
                    submission.SubmissionId,
                    submission.UserId,
                    submission.AttemptNumber,
                    submission.Files ?? [],
                    submission.FullName));
                resolvedCourseId ??= request.CourseId;
            }
        }
        else
        {
            IReadOnlyList<AssignmentSubmissionsBatch> submissionBatches;
            try
            {
                // A criação só precisa da seleção de submissões. Consultar o
                // agregador de telas aqui refazia curso, conteúdo, alunos,
                // settings e notas antes de chegar a mod_assign_get_submissions.
                // O gateway usa exatamente a função Moodle necessária e mantém
                // uma falha por atividade sem converter-a em lote vazio.
                submissionBatches = await submissionsGateway.GetAssignmentSubmissionsBatchAsync(
                    request.UserExternalId,
                    assignmentIds,
                    request.OnlyAwaitingGrading ? "submitted" : null,
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
                var failure = MoodleErrorContract.Describe(ex);
                foreach (var assignmentId in assignmentIds)
                {
                    AddDiscoveryFailure(assignmentId, ex, failure, discoveryFailures, warnings);
                }

                firstDiscoveryFailure = ex;
                submissionBatches = [];
            }

            var batchesByAssignment = submissionBatches
                .GroupBy(batch => batch.AssignmentId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            var effectiveAssignmentIds = assignmentIds.ToDictionary(
                assignmentId => assignmentId,
                assignmentId => assignmentId,
                StringComparer.OrdinalIgnoreCase);

            // mod_assign_get_submissions only accepts the assignment instance
            // ID, while Moodle users commonly copy the course-module ID (cmid).
            // Resolve only the IDs explicitly rejected as unknown, preserving
            // the fast direct path for the instance IDs returned by listings.
            var moduleIdsToResolve = assignmentIds
                .Where(assignmentId =>
                    batchesByAssignment.TryGetValue(assignmentId, out var batch) &&
                    string.Equals(batch.ErrorCode, "assignment_not_found", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (moduleIdsToResolve.Length > 0)
            {
                try
                {
                    var contents = await contentsGateway.GetCourseContentsAsync(
                        request.UserExternalId,
                        request.CourseId,
                        CourseActivityModuleTypes.Assignments,
                        includeHidden: true,
                        onlyWithFiles: false,
                        cancellationToken);
                    var instanceByModuleId = contents.Sections
                        .SelectMany(section => section.Modules)
                        .Where(module =>
                            string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(module.ModuleId) &&
                            !string.IsNullOrWhiteSpace(module.InstanceId))
                        .GroupBy(module => module.ModuleId!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First().InstanceId!, StringComparer.OrdinalIgnoreCase);
                    var resolvedPairs = moduleIdsToResolve
                        .Where(moduleId => instanceByModuleId.TryGetValue(moduleId, out var instanceId) &&
                                           !string.Equals(moduleId, instanceId, StringComparison.OrdinalIgnoreCase))
                        .Select(moduleId => new KeyValuePair<string, string>(moduleId, instanceByModuleId[moduleId]))
                        .ToArray();
                    if (resolvedPairs.Length > 0)
                    {
                        var resolvedBatches = await submissionsGateway.GetAssignmentSubmissionsBatchAsync(
                            request.UserExternalId,
                            resolvedPairs.Select(pair => pair.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                            request.OnlyAwaitingGrading ? "submitted" : null,
                            since: null,
                            before: null,
                            cancellationToken);
                        var resolvedByInstance = resolvedBatches
                            .GroupBy(batch => batch.AssignmentId, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
                        foreach (var pair in resolvedPairs)
                        {
                            if (resolvedByInstance.TryGetValue(pair.Value, out var resolvedBatch))
                            {
                                batchesByAssignment[pair.Key] = resolvedBatch;
                                effectiveAssignmentIds[pair.Key] = pair.Value;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Keep the original structured not-found result. A module
                    // lookup is a compatibility fallback and must not mask
                    // unrelated Moodle failures from the direct read.
                }
            }
            var noGradeAssignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var feedbackByAssignment = new Dictionary<string, IReadOnlyDictionary<string, AssignmentExistingGrade>>(StringComparer.OrdinalIgnoreCase);
            if (request.OnlyAwaitingGrading && settingsGateway is not null)
            {
                try
                {
                    var settings = await settingsGateway.GetCourseAssignmentSettingsAsync(
                        request.UserExternalId,
                        request.CourseId,
                        cancellationToken);
                    foreach (var assignmentId in assignmentIds)
                    {
                        if (settings.TryGetValue(assignmentId, out var assignmentSettings) &&
                            assignmentSettings.IsGradable == false)
                        {
                            noGradeAssignments.Add(assignmentId);
                        }
                    }
                }
                catch
                {
                    // Without authoritative configuration, preserve the
                    // existing grading-status behavior for numeric tasks.
                }
            }

            if (noGradeAssignments.Count > 0 && gradeReadGateway is not null)
            {
                using var feedbackLimiter = new SemaphoreSlim(4, 4);
                var studentIds = submissionBatches
                    .SelectMany(batch => batch.Submissions)
                    .Select(submission => submission.UserId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var feedbackTasks = noGradeAssignments.Select(async assignmentId =>
                {
                    await feedbackLimiter.WaitAsync(cancellationToken);
                    try
                    {
                        var grades = await gradeReadGateway.GetExistingGradesAsync(
                            request.UserExternalId,
                            effectiveAssignmentIds.GetValueOrDefault(assignmentId, assignmentId),
                            studentIds,
                            cancellationToken);
                        return (AssignmentId: assignmentId, Grades: grades, Success: true);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        return (AssignmentId: assignmentId,
                            Grades: (IReadOnlyDictionary<string, AssignmentExistingGrade>)new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase),
                            Success: false);
                    }
                    finally
                    {
                        feedbackLimiter.Release();
                    }
                }).ToArray();
                foreach (var feedback in await Task.WhenAll(feedbackTasks))
                {
                    if (feedback.Success)
                    {
                        feedbackByAssignment[feedback.AssignmentId] = feedback.Grades;
                    }
                }
            }

            foreach (var assignmentId in assignmentIds)
            {
                if (!batchesByAssignment.TryGetValue(assignmentId, out var submissionBatch))
                {
                    var missing = new MoodleApiException(
                        MoodleErrorContract.ApiError,
                        "Moodle did not return this assignment in the submissions response.",
                        functionName: "mod_assign_get_submissions");
                    AddDiscoveryFailure(assignmentId, missing, MoodleErrorContract.Describe(missing), discoveryFailures, warnings);
                    firstDiscoveryFailure ??= missing;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(submissionBatch.ErrorCode))
                {
                    var failed = new MoodleApiException(
                        submissionBatch.ErrorCode,
                        submissionBatch.ErrorMessage ?? MoodleErrorContract.SafeMessage(submissionBatch.ErrorCode),
                        functionName: "mod_assign_get_submissions");
                    AddDiscoveryFailure(assignmentId, failed, MoodleErrorContract.Describe(failed), discoveryFailures, warnings);
                    firstDiscoveryFailure ??= failed;
                    continue;
                }

                var effectiveAssignmentId = effectiveAssignmentIds[assignmentId];
                resolvedCourseId ??= request.CourseId;
                foreach (var submission in submissionBatch.Submissions)
                {
                    if (selectedItems.Count >= safeMaxItems ||
                        (selectedSubmissionIds.Count > 0 && !selectedSubmissionIds.Contains(submission.SubmissionId)) ||
                        (request.OnlyAwaitingGrading && !NeedsGrading(
                            effectiveAssignmentIds[assignmentId],
                            submission,
                            noGradeAssignments,
                            feedbackByAssignment)))
                    {
                        continue;
                    }

                    if (!selectedSubmissionKeys.Add(BuildSubmissionSelectionKey(effectiveAssignmentId, submission)))
                    {
                        continue;
                    }

                    selectedItems.Add(new AssistedGradingItemSeed(
                        request.CourseId,
                        effectiveAssignmentId,
                        submission.SubmissionId,
                        submission.UserId,
                        submission.AttemptNumber,
                        submission.Files ?? []));
                }
            }
        }

        if (selectedItems.Count == 0)
        {
            if (discoveryFailures.Count > 0)
            {
                // Preserva o MoodleApiException (e consequentemente auditId,
                // funcao e duracao) para que a camada MCP responda `error`,
                // em vez de afirmar que nao ha pendencias.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstDiscoveryFailure!).Throw();
            }

            warnings.Add("Nenhuma entrega elegivel para correcao foi encontrada nas tarefas informadas.");
            return new CreateAssistedGradingBatchResult(
                Guid.Empty,
                resolvedCourseId ?? request.CourseId,
                assignmentIds,
                TotalItems: 0,
                AcceptedItems: 0,
                BlockedItems: 0,
                Status: "NoPendingSubmissions",
                Warnings: warnings,
                DiscoveryFailures: []);
        }

        if (selectedSubmissionIds.Count > 0)
        {
            var selectedIds = selectedItems
                .Select(item => item.SubmissionId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingIds = selectedSubmissionIds
                .Where(id => !selectedIds.Contains(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingIds.Length > 0 && selectedItems.Count < safeMaxItems)
            {
                warnings.Add($"As submissões solicitadas não foram encontradas como elegíveis: {string.Join(", ", missingIds)}.");
            }
        }

        var courseId = ParsePositiveLong(resolvedCourseId ?? request.CourseId, "courseId");
        var assignmentIdsAsLong = selectedItems.Count > 0
            ? selectedItems.Select(item => ParsePositiveLong(item.AssignmentId, "assignmentId")).Distinct().ToArray()
            : assignmentIds.Select(id => ParsePositiveLong(id, "assignmentId")).Distinct().ToArray();
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);

        // O worker de correcao executa sem HttpContext. Preserve a conexao que
        // foi resolvida para esta requisicao para que ele recupere os mesmos
        // anexos e materiais Moodle, em vez de cair em uma conexao padrao ou
        // perder a identidade do cliente.
        var connectorClientId = executionContext?.ClientId;
        var connectionAlias = connectionSelection?.Alias;
        if (credentialsProvider is not null)
        {
            var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
            connectorClientId = credentials.ClientId;
            connectionAlias = credentials.Alias;
        }

        var batch = AssistedGradingBatch.Create(
            courseId,
            assignmentIdsAsLong,
            currentUser.Subject,
            moodleUserId,
            selectedItems.Count,
            teacherInstructions: request.TeacherInstructions,
            priority: request.Priority,
            includeRubric: request.IncludeRubric,
            includeSubmissionFiles: request.IncludeSubmissionFiles,
            includeCourseMaterials: request.IncludeCourseMaterials,
            connectorClientId: connectorClientId,
            connectionAlias: connectionAlias,
            idempotencyKey: normalizedIdempotencyKey,
            courseDisplayName: request.CourseDisplayName);

        await repository.AddBatchAsync(batch, cancellationToken);
        var assignmentContextCache = new Dictionary<AssignmentContextCacheKey, IReadOnlyList<ContextArtifactTemplate>>();
        var submissionFilesCache = new Dictionary<string, IReadOnlyList<AssignmentSubmissionRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in selectedItems)
        {
            var item = AssistedGradingItem.Create(
                batch.Id,
                ParsePositiveLong(seed.CourseId, "courseId"),
                ParsePositiveLong(seed.AssignmentId, "assignmentId"),
                ParseNullablePositiveLong(seed.SubmissionId, "submissionId"),
                ParsePositiveLong(seed.StudentId, "studentId"),
                seed.AttemptNumber,
                seed.StudentDisplayName);

            await repository.AddItemAsync(item, cancellationToken);
            if (request.IncludeSubmissionFiles)
            {
                await AddSubmissionFileArtifactsAsync(
                    request.UserExternalId,
                    item.Id,
                    seed.AssignmentId,
                    seed.SubmissionId,
                    seed.StudentId,
                    seed.Files,
                    submissionFilesCache,
                    warnings,
                    cancellationToken);
            }

            if ((request.IncludeRubric || request.IncludeCourseMaterials) && !_limits.DeferHeavyIngestion)
            {
                await AddAssignmentContextArtifactsAsync(
                    request.UserExternalId,
                    item,
                    assignmentContextCache,
                    request.IncludeRubric,
                    request.IncludeCourseMaterials,
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
            discoveryFailures.Count == 0 ? batch.Status.ToString() : "PartialFailure",
            warnings,
            discoveryFailures);

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
        string assignmentId,
        string? submissionId,
        string studentId,
        IReadOnlyList<AssignmentSubmissionFile> files,
        Dictionary<string, IReadOnlyList<AssignmentSubmissionRecord>> submissionFilesCache,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var maxFiles = Math.Clamp(_limits.MaxFilesPerSubmission, 0, 100);

        var effectiveFiles = files;

        // Re-fetch from Moodle API when Files came empty from snapshot/prefetch
        // but we know the submission exists. Uses per-assignment cache to avoid
        // redundant API calls for multiple students in the same assignment.
        if (effectiveFiles.Count == 0 && !string.IsNullOrWhiteSpace(assignmentId))
        {
            try
            {
                if (!submissionFilesCache.TryGetValue(assignmentId, out var cachedSubmissions))
                {
                    cachedSubmissions = await submissionsGateway.GetAssignmentSubmissionsAsync(
                        userExternalId,
                        assignmentId,
                        "submitted",
                        null,
                        null,
                        cancellationToken);
                    submissionFilesCache[assignmentId] = cachedSubmissions;
                }

                var match = cachedSubmissions.FirstOrDefault(s =>
                    (!string.IsNullOrWhiteSpace(submissionId) &&
                     string.Equals(s.SubmissionId, submissionId, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(s.UserId, studentId, StringComparison.OrdinalIgnoreCase));

                if (match?.Files is { Count: > 0 })
                {
                    effectiveFiles = match.Files;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Nao foi possivel re-obter anexos da entrega (assignment {assignmentId}, student {studentId}): {ex.Message}");
            }
        }

        // A correção assistida não materializa anexos. Persistimos somente a
        // referência autenticável que será convertida em MCP Resource no chat.
        foreach (var file in effectiveFiles.Take(maxFiles))
        {
            var sourceUrl = GradingArtifactSourceReference.Normalize(file.FileUrl);
            await repository.AddArtifactAsync(
                new GradingArtifact(
                    Guid.NewGuid(),
                    gradingItemId,
                    "submission_file",
                    file.Filename,
                    file.MimeType,
                    Sha256: null,
                    file.SizeBytes,
                    sourceUrl is null ? ExtractionStatus.Failed : ExtractionStatus.Pending,
                    ExtractedTextRef: null,
                    SummaryRef: sourceUrl is null ? "source_url_invalid" : "pending_resource",
                    DateTimeOffset.UtcNow,
                    sourceUrl),
                cancellationToken);
        }
    }

    private async Task AddAssignmentContextArtifactsAsync(
        string userExternalId,
        AssistedGradingItem item,
        Dictionary<AssignmentContextCacheKey, IReadOnlyList<ContextArtifactTemplate>> assignmentContextCache,
        bool includeRubric,
        bool includeCourseMaterials,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var cacheKey = new AssignmentContextCacheKey(
            item.CourseId,
            item.AssignmentId,
            includeRubric,
            includeCourseMaterials);
        if (!assignmentContextCache.TryGetValue(cacheKey, out var templates))
        {
            templates = await BuildAssignmentContextTemplatesAsync(
                userExternalId,
                item,
                includeRubric,
                includeCourseMaterials,
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
        bool includeRubric,
        bool includeCourseMaterials,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
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
                (_, attempt) => warnings.Add(
                    $"Falha transitória ao escanear materiais da tarefa {item.AssignmentId}; nova tentativa {attempt}."),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = MoodleErrorContract.Describe(ex);
            warnings.Add($"Nao foi possivel escanear materiais do curso para contexto da tarefa {item.AssignmentId}: {error.ErrorCode}.");
            return [ContextDiagnosticTemplate(item, "context_fetch_failed")];
        }

        var assignmentId = item.AssignmentId.ToString(CultureInfo.InvariantCulture);
        var section = contents.Sections.FirstOrDefault(candidate =>
            candidate.Modules.Any(module => IsAssignmentModule(module, assignmentId)));
        var assignmentModule = section?.Modules.FirstOrDefault(module => IsAssignmentModule(module, assignmentId));
        if (section is null || assignmentModule is null)
        {
            warnings.Add($"A tarefa {item.AssignmentId} nao foi encontrada no conteudo do curso para recuperar contexto (context_assignment_not_found).");
            return [ContextDiagnosticTemplate(item, "context_assignment_not_found")];
        }

        var templates = new List<ContextArtifactTemplate>();
        if (includeRubric && !string.IsNullOrWhiteSpace(assignmentModule.Description))
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

        if (!includeCourseMaterials)
        {
            return templates;
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
                var template = BuildContextFileArtifactTemplate(file);
                if (template is not null)
                {
                    templates.Add(template);
                }
            }
        }

        return templates;
    }

    private static ContextArtifactTemplate? BuildContextFileArtifactTemplate(CourseModuleFile file)
    {
        var filename = string.IsNullOrWhiteSpace(file.FileName)
            ? "context-file"
            : file.FileName;

        var sourceUrl = GradingArtifactSourceReference.Normalize(file.FileUrl);
        return new ContextArtifactTemplate(
            "assignment_context",
            filename,
            file.MimeType,
            Sha256: null,
            SizeBytes: file.FileSize,
            sourceUrl is null ? ExtractionStatus.Failed : ExtractionStatus.Pending,
            ExtractedTextRef: null,
            SummaryRef: sourceUrl is null ? "source_url_invalid" : "pending_resource",
            SourceUrl: sourceUrl);
    }

    private static void AddDiscoveryFailure(
        string assignmentId,
        Exception exception,
        MoodleErrorDescriptor failure,
        ICollection<AssistedGradingBatchDiscoveryFailure> discoveryFailures,
        ICollection<string> warnings)
    {
        discoveryFailures.Add(new AssistedGradingBatchDiscoveryFailure(
            assignmentId,
            failure.ErrorCode,
            failure.AuditId,
            exception is MoodleApiException moodle ? moodle.FunctionName : null,
            exception is MoodleApiException api ? api.DurationMs : null));
        warnings.Add($"Nao foi possivel listar as entregas da tarefa {assignmentId} (codigo: {failure.ErrorCode}; auditId: {failure.AuditId}).");
    }

    private static bool NeedsGrading(AssignmentSubmissionRecord submission)
    {
        return string.Equals(submission.Status, "submitted", StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(submission.GradingStatus, "notgraded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(submission.GradingStatus, "needsgrading", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(submission.GradingStatus, "notmarked", StringComparison.OrdinalIgnoreCase));
    }

    private static bool NeedsGrading(
        string assignmentId,
        AssignmentSubmissionRecord submission,
        ISet<string> noGradeAssignments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, AssignmentExistingGrade>> feedbackByAssignment)
    {
        if (!noGradeAssignments.Contains(assignmentId))
        {
            return NeedsGrading(submission);
        }

        return string.Equals(submission.Status, "submitted", StringComparison.OrdinalIgnoreCase) &&
            feedbackByAssignment.TryGetValue(assignmentId, out var grades) &&
            (!grades.TryGetValue(submission.UserId, out var grade) ||
             (!grade.HasGrade && string.IsNullOrWhiteSpace(grade.Feedback)));
    }

    private static string BuildSubmissionSelectionKey(
        string assignmentId,
        AssignmentSubmissionSummary submission)
    {
        var normalizedAssignmentId = assignmentId.Trim();
        if (!string.IsNullOrWhiteSpace(submission.SubmissionId))
        {
            return $"submission:{normalizedAssignmentId}:{submission.SubmissionId.Trim()}";
        }

        return $"student:{normalizedAssignmentId}:{submission.UserId.Trim()}:{submission.AttemptNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
    }

    private static string BuildSubmissionSelectionKey(
        string assignmentId,
        AssignmentSubmissionRecord submission)
    {
        var normalizedAssignmentId = assignmentId.Trim();
        if (!string.IsNullOrWhiteSpace(submission.SubmissionId))
        {
            return $"submission:{normalizedAssignmentId}:{submission.SubmissionId.Trim()}";
        }

        return $"student:{normalizedAssignmentId}:{submission.UserId.Trim()}:{submission.AttemptNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
    }

    private static ContextArtifactTemplate ContextDiagnosticTemplate(
        AssistedGradingItem item,
        string reason) =>
        new(
            "assignment_context",
            $"assignment-{item.AssignmentId.ToString(CultureInfo.InvariantCulture)}",
            MimeType: null,
            Sha256: null,
            SizeBytes: null,
            ExtractionStatus.Failed,
            ExtractedTextRef: null,
            SummaryRef: reason);

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
        IReadOnlyList<AssignmentSubmissionFile> Files,
        string? StudentDisplayName = null);

    private sealed record AssignmentContextCacheKey(
        long CourseId,
        long AssignmentId,
        bool IncludeRubric,
        bool IncludeCourseMaterials);

    private sealed record ContextArtifactTemplate(
        string ArtifactType,
        string? Filename,
        string? MimeType,
        string? Sha256,
        long? SizeBytes,
        string ExtractionStatus,
        string? ExtractedTextRef,
        string? SummaryRef,
        string? SourceUrl = null)
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
                DateTimeOffset.UtcNow,
                SourceUrl);
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
    IMoodleAuditLogRepository auditLogs,
    IMoodleAssignmentSettingsGateway settingsGateway)
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

        var contextSnapshot = await repository.ListLatestContextSnapshotsByItemsAsync(
            [item.Id],
            cancellationToken);
        if (!GradingContextIdentity.EnsureVersioned(item, contextSnapshot.GetValueOrDefault(item.Id)))
        {
            throw new InvalidOperationException(
                "O contexto de correcao nao esta disponivel. Gere novamente o contexto antes de revisar o item.");
        }

        var currentDraftVersionHash = GradingDraftVersionHash.Compute(item);
        if (!string.IsNullOrWhiteSpace(request.ExpectedDraftVersionHash) &&
            !string.Equals(currentDraftVersionHash, request.ExpectedDraftVersionHash, StringComparison.Ordinal))
        {
            if (MatchesExistingReview(item, request))
            {
                return await ToDetailResultAsync(item, cancellationToken);
            }

            throw new InvalidOperationException("O rascunho foi alterado desde a ultima leitura. Consulte o item novamente antes de sobrescrever.");
        }

        if (!string.Equals(item.ReviewStatus.ToString(), request.ExpectedReviewStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (MatchesExistingReview(item, request))
            {
                return await ToDetailResultAsync(item, cancellationToken);
            }

            throw new InvalidOperationException("O rascunho foi alterado desde a ultima leitura. Consulte o item novamente antes de sobrescrever.");
        }

        decimal? maxGrade = null;
        if (request.FinalGrade is not null)
        {
            AssignmentSettingsSummary? settings = null;
            try
            {
                settings = await settingsGateway.GetAssignmentSettingsAsync(
                    batch.CreatedBySubject,
                    item.CourseId.ToString(CultureInfo.InvariantCulture),
                    item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
                maxGrade = settings?.MaxGrade > 0 ? settings.MaxGrade : null;
            }
            catch
            {
                maxGrade = null;
            }

            if (maxGrade is null)
            {
                if (settings?.IsGradable == false)
                {
                    throw new InvalidOperationException("Esta atividade nao possui avaliacao numerica no Moodle. Envie somente feedback e deixe finalGrade vazio.");
                }
                throw new InvalidOperationException("A escala maxima da tarefa nao pode ser confirmada; nota numerica bloqueada.");
            }
        }

        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        item.ApplyTeacherReview(
            request.FinalGrade,
            request.FinalFeedback,
            currentUser.Subject,
            moodleUserId,
            request.TeacherDecision,
            request.ReviewNotes,
            maxGrade);

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
                request.ExpectedDraftVersionHash,
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

        var allItems = await GradingItemProcessor.LoadAllBatchItemsAsync(
            repository,
            batch.Id,
            cancellationToken);
        var metrics = BuildMetrics(batch, allItems);

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

    private static GradingBatchProcessingMetrics BuildMetrics(
        AssistedGradingBatch batch,
        IReadOnlyList<AssistedGradingItem> items)
    {
        var total = batch.TotalItems > 0 ? batch.TotalItems : 1;
        var progressPercent = (int)Math.Round((double)batch.ProcessedItems / total * 100);
        var readyPercent = (int)Math.Round((double)batch.ReadyItems / total * 100);
        var blockedPercent = (int)Math.Round((double)batch.BlockedItems / total * 100);
        var failedPercent = (int)Math.Round((double)batch.FailedItems / total * 100);
        // ProcessedItems já é a união dos estados terminais; subtrair
        // bloqueados/falhos novamente produzia uma contagem pendente inflada.
        var pendingItems = Math.Max(0, batch.TotalItems - batch.ProcessedItems);
        var canLaunch = items.Any(item =>
                item.Status == GradingItemStatus.ReadyToCommit &&
                item.CommitStatus == GradingCommitStatus.Pending &&
                !string.IsNullOrWhiteSpace(item.FinalFeedback)) &&
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

        var items = await GradingItemProcessor.LoadAllBatchItemsAsync(repository, batch.Id, cancellationToken);
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
        var awaitingAiItems = items.Count(item => item.Status == GradingItemStatus.AwaitingAiAnalysis);
        var aiDraftItems = items.Count(item =>
            item.Status == GradingItemStatus.DraftReady && item.Confidence >= 0.8m);
        var pendingReviewItems = items.Count(item =>
            item.ReviewStatus != GradingReviewStatus.Reviewed &&
            item.Status is GradingItemStatus.DraftReady or GradingItemStatus.ReadyToCommit);
        var committedItems = items.Count(item =>
            item.Status == GradingItemStatus.Committed ||
            item.CommitStatus == GradingCommitStatus.Succeeded);
        var executionUnknownItems = items.Count(item => item.CommitStatus == GradingCommitStatus.ExecutionUnknown);
        var blockedPermissionItems = items.Count(item =>
            item.CommitStatus == GradingCommitStatus.Failed &&
            (item.CommitError?.Contains("moodle.write", StringComparison.OrdinalIgnoreCase) == true ||
             item.CommitError?.Contains("feature flag", StringComparison.OrdinalIgnoreCase) == true ||
             item.CommitError?.Contains("moodle_function_unavailable", StringComparison.OrdinalIgnoreCase) == true));
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
            executionUnknownItems,
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

        if (item.CommitStatus == GradingCommitStatus.ExecutionUnknown)
        {
            reasons.Add("Resultado da escrita Moodle desconhecido; reconcilie antes de tentar novamente.");
            priority = Math.Min(priority, 0);
        }
        else if (item.CommitStatus == GradingCommitStatus.Failed)
        {
            var commitError = item.CommitError ?? item.DraftFeedback;
            if (commitError?.Contains("moodle.write", StringComparison.OrdinalIgnoreCase) == true ||
                commitError?.Contains("moodle_function_unavailable", StringComparison.OrdinalIgnoreCase) == true)
            {
                reasons.Add("Bloqueado por falta de permissao: escopo moodle.write ausente no token de servico.");
            }
            else if (commitError?.Contains("feature flag", StringComparison.OrdinalIgnoreCase) == true)
            {
                reasons.Add("Bloqueado por configuracao: feature flag de escrita de nota desabilitada.");
            }
            else
            {
                reasons.Add("Falha no lancamento Moodle: " + Shorten(commitError));
            }
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

        if (item.Status == GradingItemStatus.AwaitingAiAnalysis)
        {
            reasons.Add("Aguardando analise pela IA. Use prepare_ai_grading_batch.");
            priority = Math.Min(priority, 3);
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

        // --- Origem das correcoes ---
        builder.AppendLine();
        builder.AppendLine("## Origem das correcoes");
        builder.AppendLine();
        if (report.StatusCounts.TryGetValue("AwaitingAiAnalysis", out var awaitingAi) && awaitingAi > 0)
        {
            builder.AppendLine($"- Aguardando IA: {awaitingAi} — use `prepare_ai_grading_batch` para gerar nota e feedback.");
        }
        var aiGenerated = report.StatusCounts
            .Where(kv => kv.Key is "DraftReady" or "ReadyToCommit")
            .Sum(kv => kv.Value);
        if (aiGenerated > 0)
        {
            builder.AppendLine($"- Com feedback gerado (IA ou revisao): {aiGenerated}");
        }
        if (report.CommittedItems > 0)
        {
            builder.AppendLine($"- Lancados no Moodle: {report.CommittedItems}");
        }
        // Bloqueios por permissao
        var permissionBlocked = report.AttentionItems
            .Count(item => item.Reason.Contains("permissao", StringComparison.OrdinalIgnoreCase) ||
                           item.Reason.Contains("feature flag", StringComparison.OrdinalIgnoreCase));
        if (permissionBlocked > 0)
        {
            builder.AppendLine($"- Bloqueados por permissao/configuracao: {permissionBlocked}");
        }

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
            builder.AppendLine("- Nenhum criterio estruturado disponivel. Os criterios serao gerados pela IA durante a correcao.");
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
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser)
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
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        await orchestrator.CancelAsync(batch.Id, cancellationToken);

        return new CancelAssistedGradingBatchResult(
            batch.Id,
            batch.Status.ToString(),
            "Lote cancelado com sucesso.");
    }
}

// ============================================================
// Query para preparar contexto de correção para o LLM do chat
// ============================================================

public sealed record PrepareGradingContextForChatQuery(
    Guid GradingItemId,
    Guid? BatchJobId = null) : IRequest<GradingContextForChatResult>;

public sealed record GradingContextForChatResult(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("assignmentName")] string? AssignmentName,
    [property: JsonPropertyName("maxGrade")] decimal? MaxGrade,
    [property: JsonPropertyName("isGradable")] bool? IsGradable,
    [property: JsonPropertyName("gradingMode")] string GradingMode,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("assignmentStatement")] string? AssignmentStatement,
    [property: JsonPropertyName("studentSubmission")] string? StudentSubmission,
    [property: JsonPropertyName("extractedCriteria")] string? ExtractedCriteria,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("draftFeedback")] string? DraftFeedback,
    [property: JsonPropertyName("suggestedGrade")] decimal? SuggestedGrade,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("contextHash")] string? ContextHash = null,
    [property: JsonPropertyName("resources")] IReadOnlyList<AiGradingResourceLink>? Resources = null);

public sealed class PrepareGradingContextForChatQueryHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser,
    IMoodleAssignmentSettingsGateway settingsGateway,
    IMoodleResourceGateway? resourceGateway = null,
    IOptions<MoodleUniversalApiFeatureOptions>? resourceFeatures = null)
    : IRequestHandler<PrepareGradingContextForChatQuery, GradingContextForChatResult>
{
    public async Task<GradingContextForChatResult> Handle(
        PrepareGradingContextForChatQuery request,
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

        var warnings = new List<string>();
        var artifacts = await repository.ListArtifactsByItemAsync(item.Id, cancellationToken);

        // A correção lê os anexos originais no chat. O texto extraído local
        // não participa deste fluxo, mesmo quando existe em artifacts antigos.
        var submissionArtifacts = artifacts
            .Where(a => a.ArtifactType == "submission_file")
            .ToArray();
        var resources = new List<AiGradingResourceLink>();
        if (resourceGateway is null || resourceFeatures?.Value.McpResourceSubmissionDeliveryEnabled != true)
        {
            throw new InvalidOperationException(
                "A correção assistida exige McpResourceSubmissionDeliveryEnabled=true e o gateway MCP Resource disponível.");
        }

        if (submissionArtifacts.Length == 0)
        {
            throw new InvalidOperationException(
                "A entrega não possui anexos originais para disponibilizar como MCP Resource.");
        }
        else
        {
            foreach (var artifact in submissionArtifacts)
            {
                if (string.IsNullOrWhiteSpace(artifact.SourceUrl) || string.IsNullOrWhiteSpace(artifact.Filename))
                {
                    throw new InvalidOperationException(
                        $"O arquivo {artifact.Filename ?? "desconhecido"} não possui referência original disponível para MCP Resource.");
                }

                var descriptor = await resourceGateway.RegisterAsync(
                    new MoodleResourceRegistration(
                        "submission_attachment",
                        artifact.Filename,
                        artifact.MimeType ?? "application/octet-stream",
                        artifact.SourceUrl,
                        item.CourseId,
                        item.AssignmentId,
                        item.SubmissionId,
                        item.MoodleUserId,
                        SizeBytes: artifact.SizeBytes,
                        Sha256: artifact.Sha256),
                    cancellationToken);
                resources.Add(new AiGradingResourceLink(
                    descriptor.Uri,
                    descriptor.Filename,
                    descriptor.MimeType,
                    descriptor.SizeBytes));
            }

            warnings.Add("Entrega fornecida por MCP Resource; leia os arquivos originais antes de propor a correção.");
        }

        // Materiais binários de contexto também seguem diretamente para o chat.
        // Descrições textuais vindas da API Moodle continuam como contexto
        // textual, mas nenhum arquivo de contexto é baixado ou extraído aqui.
        foreach (var artifact in artifacts.Where(a =>
                     a.ArtifactType == "assignment_context" &&
                     !string.IsNullOrWhiteSpace(a.SourceUrl) &&
                     !string.IsNullOrWhiteSpace(a.Filename)))
        {
            try
            {
                var descriptor = await resourceGateway.RegisterAsync(
                    new MoodleResourceRegistration(
                        "assignment_context_attachment",
                        artifact.Filename!,
                        artifact.MimeType ?? "application/octet-stream",
                        artifact.SourceUrl!,
                        item.CourseId,
                        item.AssignmentId,
                        item.SubmissionId,
                        item.MoodleUserId,
                        SizeBytes: artifact.SizeBytes,
                        Sha256: artifact.Sha256),
                    cancellationToken);
                resources.Add(new AiGradingResourceLink(
                    descriptor.Uri,
                    descriptor.Filename,
                    descriptor.MimeType,
                    descriptor.SizeBytes));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                warnings.Add($"Material de contexto {artifact.Filename} não foi disponibilizado como MCP Resource ({exception.GetType().Name}).");
            }
        }

        // Texto do enunciado da atividade
        var contextArtifact = artifacts
            .Where(a => a.ArtifactType == "assignment_context" &&
                        ExtractionStatus.IsReadable(a.ExtractionStatus) &&
                        !string.IsNullOrWhiteSpace(a.ExtractedTextRef))
            .OrderByDescending(a => a.ExtractedTextRef?.Length ?? 0)
            .FirstOrDefault();
        var assignmentStatement = contextArtifact?.ExtractedTextRef;
        // Prefer the official Moodle assignment name from the settings API;
        // fall back to the context artifact filename (usually the PDF name).
        var assignmentName = contextArtifact?.Filename;

        // MaxGrade + assignment name via API Moodle
        decimal? maxGrade = null;
        bool? isGradable = null;
        try
        {
            var settings = await settingsGateway.GetAssignmentSettingsAsync(
                batch.CreatedBySubject,
                item.CourseId.ToString(CultureInfo.InvariantCulture),
                item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                cancellationToken);
            isGradable = settings?.IsGradable;
            if (settings != null && settings.MaxGrade > 0)
            {
                maxGrade = settings.MaxGrade;
            }
            else if (settings?.IsGradable == false)
            {
                warnings.Add("Esta atividade nao possui avaliacao numerica no Moodle. Envie somente feedback, sem nota.");
            }
            else
            {
                warnings.Add("Nota maxima nao encontrada via API Moodle. Sugestao numerica bloqueada; sinalize a situacao no CSV para ajuste manual.");
            }

            // Override assignmentName with the real Moodle name when available
            if (!string.IsNullOrWhiteSpace(settings?.Name))
            {
                assignmentName = settings.Name;
            }
            if (string.IsNullOrWhiteSpace(assignmentStatement) && !string.IsNullOrWhiteSpace(settings?.Description))
            {
                assignmentStatement = settings.Description;
            }
        }
        catch
        {
            warnings.Add("Falha ao buscar nota maxima via API Moodle. Sugestao numerica bloqueada; sinalize a situacao no CSV para ajuste manual.");
        }

        // Critérios extraídos (se existirem do processamento anterior)
        var evidence = await repository.ListEvidenceByItemAsync(item.Id, cancellationToken);
        var extractedCriteria = evidence.Count > 0
            ? string.Join("\n", evidence.Select(e => $"- {e.CriterionText}"))
            : null;

        var gradeInstruction = maxGrade is decimal knownMax
            ? $"A nota maxima desta atividade e {knownMax} pontos. Sugira uma nota de 0 a {knownMax}."
            : isGradable == false
                ? "Esta atividade nao possui avaliacao numerica no Moodle. Produza somente feedback qualitativo e nao sugira nota."
            : "A escala de notas desta atividade nao foi confirmada. Nao sugira nem calcule nota numerica; produza somente feedback qualitativo e sinalize a situacao no CSV para ajuste manual.";
        var instructions = AiGradingPromptPolicy.AppendUntrustedEvidenceRules(
            $"Voce e um tutor educacional. Analise a entrega do aluno comparando com o enunciado da atividade. " +
            gradeInstruction + " " +
            $"Gere um feedback pedagogico em linguagem natural (paragrafos, nao listas) que: " +
            $"1) Reconheca os pontos fortes citando elementos concretos da entrega; " +
            $"2) Indique melhorias especificas quando houver lacunas; " +
            (maxGrade is not null ? "3) Sugira uma nota somente dentro da escala confirmada. " : "3) Nao inclua nota numerica. ") +
            $"O feedback deve ser adequado para colar diretamente no Moodle. " +
            $"Nao exija saudacao nominal: este contexto fornece apenas studentId, que nao e um nome. " +
            $"Apos gerar, use save_ai_grading_batch para salvar o rascunho e export_grading_corrections_csv para receber o CSV. Nao use ferramentas de revisao, confirmacao ou envio ao Moodle.");

        return new GradingContextForChatResult(
            item.Id,
            item.BatchId,
            item.CourseId.ToString(CultureInfo.InvariantCulture),
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            assignmentName,
            maxGrade,
            isGradable,
            ResolveGradingMode(maxGrade, isGradable),
            item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            assignmentStatement,
            StudentSubmission: null,
            extractedCriteria,
            item.Status.ToString(),
            item.DraftFeedback,
            item.SuggestedGrade,
            item.Confidence,
            instructions,
            warnings,
            item.ContextHash,
            resources);
    }

    private static string ResolveGradingMode(decimal? maxGrade, bool? isGradable) =>
        isGradable switch
        {
            false => "feedback_only",
            true when maxGrade is > 0 => "numeric",
            true => "scale",
            _ => "unknown"
        };
}

// ============================================================
// Query para preparar pacote IA em lote (preparar_lote_correcao_ia)
// ============================================================

public sealed record PrepareAiGradingBatchQuery(
    Guid BatchJobId) : IRequest<AiGradingBatchPackageResult>;

public sealed record AiGradingBatchPackageResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentIds")] IReadOnlyList<string> AssignmentIds,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("items")] IReadOnlyList<AiGradingBatchItemPackage> Items,
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public sealed record AiGradingBatchItemPackage(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("assignmentName")] string? AssignmentName,
    [property: JsonPropertyName("maxGrade")] decimal? MaxGrade,
    [property: JsonPropertyName("isGradable")] bool? IsGradable,
    [property: JsonPropertyName("gradingMode")] string GradingMode,
    [property: JsonPropertyName("assignmentStatement")] string? AssignmentStatement,
    [property: JsonPropertyName("extractedCriteria")] string? ExtractedCriteria,
    [property: JsonPropertyName("extractedText")] string? ExtractedText,
    [property: JsonPropertyName("textTruncated")] bool TextTruncated,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("contextHash")] string? ContextHash = null,
    [property: JsonPropertyName("resourceDeliveryMode")] string ResourceDeliveryMode = "mcp_resource",
    [property: JsonPropertyName("resources")] IReadOnlyList<AiGradingResourceLink>? Resources = null);

public sealed record AiGradingResourceLink(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("size")] long? Size);

public sealed class PrepareAiGradingBatchQueryHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser,
    IMoodleAssignmentSettingsGateway settingsGateway,
    IGradingOperationTelemetry? telemetry = null,
    IMoodleResourceGateway? resourceGateway = null,
    IOptions<MoodleUniversalApiFeatureOptions>? resourceFeatures = null)
    : IRequestHandler<PrepareAiGradingBatchQuery, AiGradingBatchPackageResult>
{
    private const int PageSize = 100;

    public async Task<AiGradingBatchPackageResult> Handle(
        PrepareAiGradingBatchQuery request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _ = settingsGateway; // Compatibilidade de DI; a escala canônica vem do snapshot local.
        if (request.BatchJobId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(request.BatchJobId));
        }

        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        var globalWarnings = new List<string>();
        var items = await LoadAllBatchItemsAsync(batch.Id, cancellationToken);
        var itemIds = items.Select(item => item.Id).ToArray();
        var artifactsByItem = await repository.ListArtifactsByItemsAsync(itemIds, cancellationToken);
        var evidenceByItem = await repository.ListEvidenceByItemsAsync(itemIds, cancellationToken);
        var snapshotsByItem = await repository.ListLatestContextSnapshotsByItemsAsync(itemIds, cancellationToken);
        var packageItems = new List<AiGradingBatchItemPackage>();
        var eligibleItems = items
            .Where(item => item.Status == GradingItemStatus.AwaitingAiAnalysis)
            .ToArray();
        var skippedByStatus = items
            .Where(item => item.Status != GradingItemStatus.AwaitingAiAnalysis)
            .GroupBy(item => item.Status.ToString())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Count()} {FormatSkippedStatus(group.Key)}")
            .ToArray();
        if (skippedByStatus.Length > 0)
        {
            globalWarnings.Add(
                $"Itens fora da pre-validacao da IA foram ignorados: {string.Join(", ", skippedByStatus)}.");
        }

        foreach (var item in eligibleItems)
        {
            var itemWarnings = new List<string>();
            IReadOnlyList<GradingArtifact> artifacts = artifactsByItem.GetValueOrDefault(item.Id, []);
            var resourceLinks = new List<AiGradingResourceLink>();
            if (resourceGateway is null || resourceFeatures?.Value.McpResourceSubmissionDeliveryEnabled != true)
            {
                throw new InvalidOperationException(
                    "A correção assistida exige McpResourceSubmissionDeliveryEnabled=true e o gateway MCP Resource disponível.");
            }

            var submissionArtifacts = artifacts
                .Where(artifact => artifact.ArtifactType == "submission_file")
                .ToArray();
            if (submissionArtifacts.Length == 0 || submissionArtifacts.Any(artifact =>
                    string.IsNullOrWhiteSpace(artifact.SourceUrl) ||
                    string.IsNullOrWhiteSpace(artifact.Filename)))
            {
                throw new InvalidOperationException(
                    $"A entrega do item {item.Id} não possui referências originais completas para MCP Resource.");
            }

            try
            {
                foreach (var artifact in submissionArtifacts)
                {
                    var descriptor = await resourceGateway.RegisterAsync(
                        new MoodleResourceRegistration(
                            "submission_attachment",
                            artifact.Filename!,
                            artifact.MimeType ?? "application/octet-stream",
                            artifact.SourceUrl!,
                            item.CourseId,
                            item.AssignmentId,
                            item.SubmissionId,
                            item.MoodleUserId,
                            SizeBytes: artifact.SizeBytes,
                            Sha256: artifact.Sha256),
                        cancellationToken);
                    resourceLinks.Add(new AiGradingResourceLink(
                        descriptor.Uri,
                        descriptor.Filename,
                        descriptor.MimeType,
                        descriptor.SizeBytes));
                }

                foreach (var artifact in artifacts.Where(a =>
                             a.ArtifactType == "assignment_context" &&
                             !string.IsNullOrWhiteSpace(a.SourceUrl) &&
                             !string.IsNullOrWhiteSpace(a.Filename)))
                {
                    try
                    {
                        var descriptor = await resourceGateway.RegisterAsync(
                            new MoodleResourceRegistration(
                                "assignment_context_attachment",
                                artifact.Filename!,
                                artifact.MimeType ?? "application/octet-stream",
                                artifact.SourceUrl!,
                                item.CourseId,
                                item.AssignmentId,
                                item.SubmissionId,
                                item.MoodleUserId,
                                SizeBytes: artifact.SizeBytes,
                                Sha256: artifact.Sha256),
                            cancellationToken);
                        resourceLinks.Add(new AiGradingResourceLink(
                            descriptor.Uri,
                            descriptor.Filename,
                            descriptor.MimeType,
                            descriptor.SizeBytes));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        itemWarnings.Add($"Material de contexto {artifact.Filename} não foi disponibilizado como MCP Resource ({exception.GetType().Name}).");
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                resourceLinks.Clear();
                throw new InvalidOperationException(
                    $"Não foi possível registrar os anexos da submissão como MCP Resource ({exception.GetType().Name}).",
                    exception);
            }

            itemWarnings.Add("Entrega fornecida por MCP Resource; leia os arquivos originais antes de propor a correção.");

            // Contexto da atividade
            var contextArtifact = artifacts
                .Where(a => a.ArtifactType == "assignment_context" &&
                            ExtractionStatus.IsReadable(a.ExtractionStatus) &&
                            !string.IsNullOrWhiteSpace(a.ExtractedTextRef))
                .OrderByDescending(a => a.ExtractedTextRef?.Length ?? 0)
                .FirstOrDefault();
            var assignmentStatement = contextArtifact?.ExtractedTextRef;
            var assignmentName = contextArtifact?.Filename;

            // Critérios (evidências do processamento anterior, se existirem)
            var evidence = evidenceByItem.GetValueOrDefault(item.Id, []);
            var extractedCriteria = evidence.Count > 0
                ? string.Join("\n", evidence.Select(e => $"- {e.CriterionText}"))
                : null;

            // Escala, nome e enunciado vêm do snapshot canônico publicado pelo
            // worker. A preparação da IA nunca reconstrói contexto no Moodle.
            var snapshot = snapshotsByItem.GetValueOrDefault(item.Id);
            var snapshotData = ParseSnapshotDisplay(snapshot?.PayloadJson);
            assignmentName = snapshotData.ActivityName ?? assignmentName;
            assignmentStatement ??= snapshotData.AssignmentStatement;
            var maxGrade = snapshotData.MaxGrade;
            var isGradable = snapshotData.IsGradable;
            itemWarnings.AddRange(snapshotData.Warnings);

            if (maxGrade is null)
            {
                itemWarnings.Add(isGradable == false
                    ? "Atividade sem avaliacao numerica no Moodle. Gere somente feedback, sem nota."
                    : "Nota maxima nao encontrada via API Moodle. Sugestao numerica bloqueada; sinalize a situacao no CSV para ajuste manual.");
            }

            packageItems.Add(new AiGradingBatchItemPackage(
                item.Id,
                item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
                item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
                assignmentName,
                maxGrade,
                isGradable,
                snapshotData.GradingMode,
                assignmentStatement,
                extractedCriteria,
                ExtractedText: null,
                TextTruncated: false,
                itemWarnings,
                item.ContextHash,
                ResourceDeliveryMode: "mcp_resource",
                resourceLinks));
        }

        if (packageItems.Count == 0)
        {
            globalWarnings.Add("Nenhum item apto para analise pela IA foi encontrado no lote.");
        }

        var instructions = AiGradingPromptPolicy.AppendUntrustedEvidenceRules(
            "Voce e um tutor educacional. Para cada aluno no pacote, analise a entrega comparando com o enunciado e criterios da atividade. " +
            "Gere um feedback curto e direto (maximo 6 paragrafos) seguindo esta estrutura exata:\n" +
            "1) SAUDACAO: use uma saudacao neutra. Este pacote fornece apenas studentId; nunca trate esse identificador como nome e nunca invente ou derive um nome a partir dele.\n" +
            "2) RECONHECIMENTO: agradeca a entrega em uma frase curta.\n" +
            "3) PONTO POSITIVO: destaque algo concreto que o aluno fez bem na entrega.\n" +
            "4) MELHORIAS: indique de forma clara e respeitosa os pontos que precisam ser revistos ou aprofundados. Seja especifico sobre o que ajustar, sem frases genericas como 'esta errado'. Se houver lacuna, sugira que o aluno revise os conceitos relacionados no material de apoio do curso, sem citar nomes de aulas ou documentos especificos.\n" +
            "5) INCENTIVO: finalize com uma mensagem motivadora curta.\n" +
            "6) ENCERRAMENTO: encerre com 'Em caso de duvidas, estou a disposicao.'\n\n" +
            "Regras de estilo:\n" +
            "- Use linguagem clara, respeitosa e objetiva.\n" +
            "- Tom acolhedor e profissional. Sem julgamentos pessoais, ironias ou comparacoes.\n" +
            "- Escreva em paragrafos (nao use listas com marcadores).\n" +
            "- O feedback inteiro deve ter entre 80 e 200 palavras.\n" +
            "- Atribua nota numerica somente quando maxGrade estiver informado; caso contrario, nao inclua nota.\n" +
            "O feedback deve ser adequado para colar diretamente no Moodle. " +
            "Apos gerar, use a tool save_ai_grading_batch para salvar os resultados e em seguida chame export_grading_corrections_csv para receber o CSV com nome, nota, feedback e situacao. Nao chame ferramentas de confirmacao ou envio ao Moodle.");

        var result = new AiGradingBatchPackageResult(
            batch.Id,
            batch.CourseId.ToString(CultureInfo.InvariantCulture),
            batch.AssignmentIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray(),
            packageItems.Count,
            packageItems,
            instructions,
            globalWarnings);
        telemetry?.RecordPhase(
            "grading",
            "package",
            "success",
            stopwatch.Elapsed.TotalMilliseconds,
            queryCount: 5,
            itemCount: packageItems.Count);
        return result;
    }

    private Task<IReadOnlyList<AssistedGradingItem>> LoadAllBatchItemsAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        return GradingItemProcessor.LoadAllBatchItemsAsync(repository, batchId, cancellationToken);
    }

    private static string FormatSkippedStatus(string status) => status switch
    {
        "Blocked" => "bloqueado(s)",
        "Failed" => "falho(s)",
        "Pending" => "aguardando processamento",
        "DraftReady" => "com rascunho pronto para CSV",
        "ReadyToCommit" => "pronto para CSV",
        "Committed" => "ja lancado(s) no Moodle",
        _ => status
    };

    private static SnapshotDisplayData ParseSnapshotDisplay(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new SnapshotDisplayData(null, null, null, null, "unknown", ["Snapshot de contexto ainda não publicado; escala não confirmada."]);
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var activityName = GetString(root, "ActivityName");
            var statement = GetString(root, "AssignmentStatement");
            decimal? maxGrade = null;
            bool? isGradable = null;
            var gradingMode = "unknown";
            if (TryGetProperty(root, "GradingScale", out var scale) && scale.ValueKind == JsonValueKind.Object)
            {
                maxGrade = GetDecimal(scale, "MaximumGrade");
                gradingMode = GetString(scale, "GradingMode")?.Trim().ToLowerInvariant() switch
                {
                    "numeric" => "numeric",
                    "scale" => "scale",
                    "feedback_only" => "feedback_only",
                    _ when maxGrade is > 0m => "numeric",
                    _ when !string.IsNullOrWhiteSpace(GetString(scale, "Name")) ||
                           !string.IsNullOrWhiteSpace(GetString(scale, "Description")) => "scale",
                    _ => "unknown"
                };
                isGradable = gradingMode switch
                {
                    "feedback_only" => false,
                    "numeric" or "scale" => true,
                    _ => null
                };
            }

            var warnings = GetStringArray(root, "Warnings").Concat(GetStringArray(root, "Blockers"))
                .Distinct(StringComparer.Ordinal).ToArray();
            return new SnapshotDisplayData(activityName, statement, maxGrade, isGradable, gradingMode, warnings);
        }
        catch (JsonException)
        {
            return new SnapshotDisplayData(null, null, null, null, "unknown", ["Snapshot de contexto inválido; escala não confirmada."]);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? GetDecimal(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? number
            : null;

    private static IEnumerable<string> GetStringArray(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Where(item => !string.IsNullOrWhiteSpace(item))
            : [];

    private sealed record SnapshotDisplayData(
        string? ActivityName,
        string? AssignmentStatement,
        decimal? MaxGrade,
        bool? IsGradable,
        string GradingMode,
        IReadOnlyList<string> Warnings);
}

// ============================================================
// Command para salvar correções geradas pela IA (salvar_correcoes_ia_lote)
// ============================================================

public sealed record SaveAiGradingBatchCommand(
    Guid BatchJobId,
    IReadOnlyList<AiGradingItemInput> Items) : IRequest<SaveAiGradingBatchResult>;

public sealed record AiGradingItemInput(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    // Mantido apenas para compatibilidade com clientes legados. O nome não é
    // fonte de identidade nem é persistido: o item usa somente MoodleUserId.
    [property: JsonPropertyName("nome")] string? Nome,
    [property: JsonPropertyName("nota")] decimal? Nota,
    [property: JsonPropertyName("feedback")] string Feedback,
    [property: JsonPropertyName("proposal")] AiGradingProposalInput? Proposal = null);

public sealed record SaveAiGradingBatchResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("savedItems")] int SavedItems,
    [property: JsonPropertyName("skippedItems")] int SkippedItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("nextStep")] string NextStep,
    [property: JsonPropertyName("updatedItems")] IReadOnlyList<SaveAiGradingBatchItemResult>? UpdatedItems = null);

public sealed record SaveAiGradingBatchItemResult(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("draftVersionHash")] string DraftVersionHash,
    [property: JsonPropertyName("suggestedGrade")] decimal? SuggestedGrade,
    [property: JsonPropertyName("draftFeedback")] string? DraftFeedback);

public sealed class SaveAiGradingBatchCommandHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser,
    IMoodleUserResolver moodleUserResolver,
    IMoodleAuditLogRepository auditLogs,
    IMoodleAssignmentSettingsGateway settingsGateway,
    IGradingProposalStore? proposalStore = null,
    IGradingOperationTelemetry? telemetry = null,
    IMoodleResourceRepository? resourceRepository = null,
    ISubmissionContentHashResolver? submissionContentHashResolver = null,
    IOptions<MoodleUniversalApiFeatureOptions>? resourceFeatures = null)
    : IRequestHandler<SaveAiGradingBatchCommand, SaveAiGradingBatchResult>
{
    // O contrato atual de salvar_correcoes_ia_lote é legado: não transporta
    // evidências, cobertura nem confiança calculada. Portanto, ele nunca pode
    // promover uma nota para alta confiança por presunção. A revisão humana
    // continua obrigatória e a confiança é explicitamente zero até que uma
    // proposta versionada seja integrada ao pipeline.
    private const decimal LegacyAiProposalConfidence = 0m;

    public async Task<SaveAiGradingBatchResult> Handle(
        SaveAiGradingBatchCommand request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (request.BatchJobId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(request.BatchJobId));
        }

        if (request.Items.Count == 0)
        {
            throw new ArgumentException("Informe pelo menos um item.", nameof(request.Items));
        }

        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        _ = settingsGateway; // Compatibilidade de DI; a escala vem do snapshot local.
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        var warnings = new List<string>();
        var savedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var proposalsToPublish = new List<AiGradingProposal>();
        var updatedItems = new List<SaveAiGradingBatchItemResult>();
        var snapshotsByItem = await repository.ListLatestContextSnapshotsByItemsAsync(
            request.Items.Select(input => input.GradingItemId).Where(id => id != Guid.Empty).Distinct().ToArray(),
            cancellationToken);
        var legacySettingsCache = new Dictionary<long, AssignmentSettingsSummary?>();
        var itemsById = await repository.GetItemsAsync(
            request.Items.Select(input => input.GradingItemId).Where(id => id != Guid.Empty).Distinct().ToArray(),
            cancellationToken);
        var nextProposalVersions = proposalStore is null
            ? new Dictionary<Guid, int>()
            : new Dictionary<Guid, int>(await proposalStore.GetNextVersionsAsync(itemsById.Keys.ToArray(), cancellationToken));

        foreach (var input in request.Items)
        {
            try
            {
                if (input.GradingItemId == Guid.Empty)
                {
                    warnings.Add($"Item ignorado: gradingItemId vazio.");
                    skippedCount++;
                    continue;
                }

                if (input.Proposal is null && string.IsNullOrWhiteSpace(input.Feedback))
                {
                    warnings.Add($"Item {input.GradingItemId} ignorado: feedback vazio.");
                    skippedCount++;
                    continue;
                }

                if (input.Nota is < 0 || input.Proposal?.SuggestedGrade is < 0)
                {
                    warnings.Add($"Item {input.GradingItemId} ignorado: nota negativa nao e permitida.");
                    skippedCount++;
                    continue;
                }

                if (!itemsById.TryGetValue(input.GradingItemId, out var item))
                {
                    warnings.Add($"Item {input.GradingItemId} nao encontrado.");
                    failedCount++;
                    continue;
                }

                if (item.BatchId != batch.Id)
                {
                    warnings.Add($"Item {input.GradingItemId} nao pertence ao lote {batch.Id}.");
                    failedCount++;
                    continue;
                }

                // Itens bloqueados ou falhos permanecem no relatorio manual;
                // um rascunho da IA nao pode reabrir uma submissao sem leitura valida.
                if (item.Status is GradingItemStatus.Blocked or GradingItemStatus.Failed)
                {
                    warnings.Add($"Item {input.GradingItemId} ignorado: status {item.Status} ({item.DraftFeedback ?? item.CommitError ?? "sem detalhe"}).");
                    skippedCount++;
                    continue;
                }

                // Pular itens que já foram revisados ou commitados
                if (item.Status is GradingItemStatus.ReadyToCommit or GradingItemStatus.Committed)
                {
                    warnings.Add($"Item {input.GradingItemId} ja foi revisado/commitado. Ignorado.");
                    skippedCount++;
                    continue;
                }

                if (item.Status is not (GradingItemStatus.AwaitingAiAnalysis or GradingItemStatus.DraftReady))
                {
                    warnings.Add($"Item {input.GradingItemId} ignorado: aguardando pre-validacao antes da analise pela IA.");
                    skippedCount++;
                    continue;
                }

                if (input.Proposal is not null &&
                    string.IsNullOrWhiteSpace(input.Proposal.Feedback) &&
                    string.IsNullOrWhiteSpace(input.Feedback) &&
                    string.IsNullOrWhiteSpace(item.DraftFeedback))
                {
                    warnings.Add($"Item {input.GradingItemId} ignorado: feedback da proposta vazio.");
                    skippedCount++;
                    continue;
                }

                var requestedGrade = input.Proposal?.SuggestedGrade ?? input.Nota;
                var maxGrade = TryReadMaxGrade(snapshotsByItem.GetValueOrDefault(item.Id)?.PayloadJson);
                if (maxGrade is null && requestedGrade is not null && !snapshotsByItem.ContainsKey(item.Id))
                {
                    // Compatibilidade para lotes legados que ainda não têm
                    // snapshot. Lotes novos nunca chegam a este fallback.
                    if (!legacySettingsCache.TryGetValue(item.AssignmentId, out var legacySettings))
                    {
                        legacySettings = await settingsGateway.GetAssignmentSettingsAsync(
                            batch.CreatedBySubject,
                            item.CourseId.ToString(CultureInfo.InvariantCulture),
                            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                            cancellationToken);
                        legacySettingsCache[item.AssignmentId] = legacySettings;
                    }
                    maxGrade = legacySettings?.MaxGrade > 0 ? legacySettings.MaxGrade : null;
                    if (maxGrade is not null)
                    {
                        warnings.Add($"Item {input.GradingItemId}: escala obtida pelo fallback legado; publique um snapshot para eliminar a leitura Moodle.");
                    }
                }
                if (requestedGrade is not null)
                {
                    if (maxGrade is null)
                    {
                        warnings.Add($"Item {input.GradingItemId} ignorado: escala maxima nao confirmada; nota numerica bloqueada.");
                        skippedCount++;
                        continue;
                    }

                    if (requestedGrade > maxGrade)
                    {
                        warnings.Add($"Item {input.GradingItemId} ignorado: nota excede a escala maxima confirmada.");
                        skippedCount++;
                        continue;
                    }
                }

                AiGradingProposal? proposal = null;
                if (input.Proposal is not null)
                {
                    if (input.Proposal.Version <= 0)
                    {
                        warnings.Add($"Item {input.GradingItemId} ignorado: versao da proposta invalida.");
                        skippedCount++;
                        continue;
                    }

                    if (input.Nota is not null && input.Nota != input.Proposal.SuggestedGrade)
                    {
                        warnings.Add($"Item {input.GradingItemId} ignorado: nota legada diverge da proposta versionada.");
                        skippedCount++;
                        continue;
                    }

                    var proposalVersion = proposalStore is null
                        ? input.Proposal.Version
                        : nextProposalVersions.GetValueOrDefault(item.Id, input.Proposal.Version);
                    string? submissionContentHash = null;
                    if (resourceFeatures?.Value.McpGradingDraftEnabled == true)
                    {
                        if (resourceRepository is null || submissionContentHashResolver is null)
                            throw new InvalidOperationException("A integridade de draft MCP nao esta configurada.");
                        if (item.SubmissionId is not long submissionId || moodleUserId is null)
                            throw new InvalidOperationException("A submissao Moodle nao esta identificada para selar o draft.");

                        var resourceUris = (input.Proposal.ResourceUris ?? [])
                            .Concat((input.Proposal.Evidence ?? [])
                                .Select(evidence => evidence.ResourceUri)
                                .Where(uri => !string.IsNullOrWhiteSpace(uri))
                                .Select(uri => uri!))
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();
                        var resources = new Dictionary<string, MoodleResource>(StringComparer.Ordinal);
                        foreach (var uri in resourceUris)
                        {
                            if (!MoodleResourceUri.TryParse(uri, out var resourceId))
                                throw new InvalidOperationException("A proposta aponta para uma URI de resource invalida.");
                            var resource = await resourceRepository.FindAsync(resourceId, cancellationToken);
                            if (resource is null || resource.IsExpired(DateTimeOffset.UtcNow) || resource.SubmissionId != item.SubmissionId)
                                throw new InvalidOperationException("A proposta referencia um resource invalido da submissao.");
                            if (!string.IsNullOrWhiteSpace(resource.ParentResourceId))
                            {
                                resource = await resourceRepository.FindAsync(resource.ParentResourceId, cancellationToken)
                                    ?? throw new InvalidOperationException("O resource ZIP pai nao esta mais disponivel.");
                            }
                            if (resource.IsExpired(DateTimeOffset.UtcNow) || string.IsNullOrWhiteSpace(resource.Sha256))
                                throw new InvalidOperationException("Todos os resources da proposta devem ser lidos e validados antes de salvar o draft.");
                            resources[resource.ResourceId] = resource;
                        }

                        var integrity = await submissionContentHashResolver.ResolveAsync(
                            moodleUserId.Value.ToString(CultureInfo.InvariantCulture),
                            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
                            submissionId,
                            resources.Values.Select(resource => resource.Sha256!).ToArray(),
                            cancellationToken);
                        if (resources.Count != integrity.FileCount)
                            throw new InvalidOperationException("O draft MCP deve vincular todos os anexos originais da submissao.");

                        submissionContentHash = integrity.Hash;
                        item.RecordSubmissionIntegrity(submissionContentHash, resources.Keys.ToArray());
                    }
                    else if (resourceRepository is not null)
                    {
                        // Mesmo no modo legado, URIs de evidência são sempre
                        // verificadas; o ID opaco não é uma autorização.
                        foreach (var evidence in input.Proposal.Evidence ?? [])
                        {
                            if (string.IsNullOrWhiteSpace(evidence.ResourceUri)) continue;
                            if (!MoodleResourceUri.TryParse(evidence.ResourceUri, out var resourceId)) throw new InvalidOperationException("A evidencia aponta para uma URI de resource invalida.");
                            var resource = await resourceRepository.FindAsync(resourceId, cancellationToken);
                            if (resource is null || resource.IsExpired(DateTimeOffset.UtcNow) || resource.SubmissionId != item.SubmissionId)
                                throw new InvalidOperationException("A evidencia nao esta vinculada a um resource valido da submissao.");
                        }
                    }
                    proposal = AiGradingProposalFactory.Create(
                        item,
                        input.Proposal,
                        maxGrade,
                        proposalVersion,
                        input.Feedback,
                        submissionContentHash);
                    if (proposalStore is not null)
                    {
                        proposalsToPublish.Add(proposal);
                    }
                }
                else if (proposalStore is not null)
                {
                    var legacyVersion = nextProposalVersions.GetValueOrDefault(item.Id, 1);
                    proposalsToPublish.Add(AiGradingProposal.FromLegacy(
                        item.Id,
                        item.BatchId,
                        legacyVersion,
                        item.ContextHash,
                        input.Nota,
                        input.Feedback));
                }

                item.SetDraft(
                    suggestedGrade: proposal?.SuggestedGrade ?? input.Nota,
                    confidence: proposal?.Confidence ?? LegacyAiProposalConfidence,
                    draftFeedback: proposal?.Feedback ?? input.Feedback,
                    privateNotesToTeacher: proposal is null && input.Nota is null
                        ? "Proposta IA legada sem evidencias ou confianca calculada; escala numerica nao confirmada. Revise a escala no CSV antes de qualquer uso posterior."
                        : proposal is null
                            ? "Proposta IA legada sem evidencias ou confianca calculada. Revise o resultado no CSV antes de qualquer uso posterior."
                            : proposal.ReviewRequired
                                ? $"Proposta IA versionada ({proposal.ProposalHash[..12]}) requer revisao manual no CSV: {string.Join(", ", proposal.UncertaintyReasons)}."
                                : $"Proposta IA versionada ({proposal.ProposalHash[..12]}) requer revisao manual no CSV antes de qualquer uso posterior.",
                    maxGrade: maxGrade);

                savedCount++;
                updatedItems.Add(new SaveAiGradingBatchItemResult(
                    item.Id,
                    item.Status.ToString(),
                    GradingDraftVersionHash.Compute(item),
                    item.SuggestedGrade,
                    item.DraftFeedback));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Falha ao salvar item {input.GradingItemId}: {ex.Message}");
                failedCount++;
            }
        }

        if (proposalStore is not null)
        {
            await proposalStore.PublishManyAsync(proposalsToPublish, cancellationToken);
        }

        // Atualizar contadores do lote
        var allItems = await GradingItemProcessor.LoadAllBatchItemsAsync(repository, batch.Id, cancellationToken);
        GradingItemProcessor.UpdateBatchCounters(batch, allItems);
        await repository.SaveChangesAsync(cancellationToken);

        await auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = $"grading-batch-{batch.Id:N}",
            BatchJobId = batch.Id,
            ToolName = "salvar_correcoes_ia_lote",
            RiskLevel = ToolRiskLevel.DraftOnly,
            ActorSubject = currentUser.Subject,
            ActorEmail = currentUser.Email,
            ActorMoodleUserId = moodleUserId,
            CourseId = batch.CourseId,
            MoodleFunction = null,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                batchJobId = batch.Id,
                itemCount = request.Items.Count
            }),
            ResponseSummaryJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                savedCount,
                skippedCount,
                failedCount
            }),
            Status = "ai_drafts_saved"
        }, cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        var result = new SaveAiGradingBatchResult(
            batch.Id,
            savedCount,
            skippedCount,
            failedCount,
            request.Items.Count,
            warnings,
            NextStep: savedCount > 0
                ? "Correcoes salvas internamente. Para CSV externo, chame export_grading_corrections_csv. Para publicar no Moodle, chame create_batch_grade_launch_preview, revise a previa e aguarde a confirmacao explicita."
                : "Nenhum item foi salvo. Verifique os avisos.",
            updatedItems);
        telemetry?.RecordPhase(
            "grading",
            "save",
            failedCount > 0 ? "partial_failure" : "success",
            stopwatch.Elapsed.TotalMilliseconds,
            queryCount: 5,
            itemCount: savedCount);
        return result;
    }

    private static decimal? TryReadMaxGrade(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!document.RootElement.TryGetProperty("GradingScale", out var scale) &&
                !document.RootElement.TryGetProperty("gradingScale", out scale)) return null;
            if (scale.ValueKind != JsonValueKind.Object) return null;
            if (!scale.TryGetProperty("MaximumGrade", out var value) &&
                !scale.TryGetProperty("maximumGrade", out value)) return null;
            return value.ValueKind == JsonValueKind.Number &&
                value.TryGetDecimal(out var max) &&
                max > 0
                    ? max
                    : null;
        }
        catch (JsonException) { return null; }
    }
}
