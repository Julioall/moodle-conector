using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

public sealed record CreateGradingLaunchPreviewCommand(
    Guid BatchJobId,
    IReadOnlyList<Guid> GradingItemIds,
    bool OnlyReviewed,
    bool AllowOverwriteExisting = false) : IRequest<CreateGradingLaunchPreviewResult>;

public sealed record CreateGradingLaunchPreviewResult(
    [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("readyItems")] int ReadyItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("launches")] IReadOnlyList<GradingLaunchPreviewItem> Launches,
    [property: JsonPropertyName("confirmationText")] string ConfirmationText,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public sealed record GradingLaunchPreviewItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("grade")] decimal Grade,
    [property: JsonPropertyName("feedbackText")] string FeedbackText,
    [property: JsonPropertyName("contextHash")] string? ContextHash = null);

public sealed record ConfirmMoodleBatchLaunchCommand(
    Guid PendingActionId,
    string ConfirmationText) : IRequest<ConfirmMoodleBatchLaunchResult>;

public sealed record ConfirmMoodleBatchLaunchResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("pendingActionId")] Guid PendingActionId,
    [property: JsonPropertyName("sentItems")] int SentItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("failures")] IReadOnlyList<GradingLaunchFailure> Failures,
    [property: JsonPropertyName("auditId")] string? AuditId);

public sealed record GradingLaunchFailure(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("message")] string Message);

public sealed record GradingLaunchPayload(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("items")] IReadOnlyList<GradingLaunchPayloadItem> Items,
    [property: JsonPropertyName("allowOverwriteExisting")] bool AllowOverwriteExisting = false);

public sealed record GradingLaunchPayloadItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("grade")] decimal Grade,
    [property: JsonPropertyName("feedbackText")] string FeedbackText,
    [property: JsonPropertyName("attemptNumber")] int? AttemptNumber,
    [property: JsonPropertyName("draftVersionHash")] string DraftVersionHash,
    [property: JsonPropertyName("contextHash")] string? ContextHash = null);

public sealed record AssignmentExistingGrade(
    string AssignmentId,
    string StudentId,
    decimal? Grade,
    bool HasGrade,
    string? Feedback = null,
    decimal? GradeMax = null);

public sealed record AssignmentSubmissionAttemptStatus(
    string AssignmentId,
    string StudentId,
    int? AttemptNumber,
    string? SubmissionStatus,
    bool HasFeedback = false);

public sealed class CreateGradingLaunchPreviewCommandHandler(
    IGradingReviewRepository repository,
    IPendingActionService pendingActions,
    ICurrentUserContext currentUser,
    IMoodleAssignmentSettingsGateway settingsGateway)
    : IRequestHandler<CreateGradingLaunchPreviewCommand, CreateGradingLaunchPreviewResult>
{
    private const string ToolName = "criar_previa_lancamento_lote";
    private static readonly TimeSpan PendingActionExpiration = TimeSpan.FromMinutes(15);

    public async Task<CreateGradingLaunchPreviewResult> Handle(
        CreateGradingLaunchPreviewCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);
        var allItems = await LoadBatchItemsAsync(batch.Id, cancellationToken);
        var selectedIds = request.GradingItemIds.ToHashSet();
        var selected = selectedIds.Count == 0
            ? allItems
            : allItems.Where(item => selectedIds.Contains(item.Id)).ToArray();
        var launchable = selected
            .Where(item => IsReadyForLaunch(item, request.OnlyReviewed))
            .ToArray();
        var scaleWarnings = new List<string>();
        var contextWarnings = new List<string>();
        var ready = new List<AssistedGradingItem>();
        var settingsCache = new Dictionary<long, AssignmentSettingsSummary?>();
        foreach (var item in launchable)
        {
            if (!HasVersionedContext(item))
            {
                contextWarnings.Add(
                    $"Item {item.Id}: contexto de correcao ausente ou legado; gere uma nova previa antes do lancamento.");
                continue;
            }

            var maxGrade = await GetKnownMaxGradeAsync(batch, item, settingsCache, cancellationToken);
            if (maxGrade is null)
            {
                scaleWarnings.Add(
                    $"Item {item.Id}: nota maxima da atividade nao foi confirmada; lancamento numerico bloqueado.");
                continue;
            }

            if (item.FinalGrade > maxGrade)
            {
                scaleWarnings.Add(
                    $"Item {item.Id}: nota final {FormatGrade(item.FinalGrade!.Value)} excede nota maxima {FormatGrade(maxGrade.Value)} identificada pelos criterios.");
                continue;
            }

            ready.Add(item);
        }

        var blocked = selected.Count - ready.Count;

        if (ready.Count == 0)
        {
            return new CreateGradingLaunchPreviewResult(
                Guid.Empty,
                batch.Id,
                selected.Count,
                ReadyItems: 0,
                blocked,
                Launches: [],
                ConfirmationText: string.Empty,
                ExpiresAt: null,
                Warnings: scaleWarnings.Count > 0 || contextWarnings.Count > 0
                    ? [.. contextWarnings, .. scaleWarnings]
                    : ["Nenhum item revisado e pronto para lancamento foi encontrado."]);
        }

        var payload = new GradingLaunchPayload(
            batch.Id,
            ready.Select(ToPayloadItem).ToArray(),
            request.AllowOverwriteExisting);
        var previewItems = ready.Select(ToPreviewItem).ToArray();
        var itemLabel = ready.Count == 1 ? "CORRECAO" : "CORRECOES";
        var activityScope = string.Join(
            ",",
            ready
                .Select(item => item.AssignmentId.ToString(CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal)
                .Take(10));
        var overwriteScope = request.AllowOverwriteExisting
            ? " E AUTORIZO SOBRESCREVER NOTAS E FEEDBACKS EXISTENTES"
            : string.Empty;
        var confirmationText =
            $"CONFIRMO O LANCAMENTO DE {ready.Count} {itemLabel} NO MOODLE PARA O LOTE {batch.Id} DO CURSO {batch.CourseId} NAS ATIVIDADES {activityScope} COM ESCOPO NOTA_E_FEEDBACK{overwriteScope}";
        var pending = await pendingActions.CreatePendingActionAsync(
            ToolName,
            ToolRiskLevel.CriticalHumanConfirmedWrite,
            payload,
            new
            {
                batchJobId = batch.Id,
                totalItems = selected.Count,
                readyItems = ready.Count,
                blockedItems = blocked,
                launches = previewItems
            },
            confirmationText,
            PendingActionExpiration,
            batch.CourseId,
            cancellationToken);

        return new CreateGradingLaunchPreviewResult(
            pending.PendingActionId,
            batch.Id,
            selected.Count,
            ready.Count,
            blocked,
            previewItems,
            pending.ConfirmationText,
            pending.ExpiresAt,
            Warnings: BuildWarnings(blocked, scaleWarnings, contextWarnings));
    }

    private async Task<IReadOnlyList<AssistedGradingItem>> LoadBatchItemsAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var total = await repository.CountItemsByBatchAsync(batchId, cancellationToken);
        return await GradingItemProcessor.LoadAllBatchItemsAsync(
            repository,
            batchId,
            cancellationToken,
            Math.Max(1, total));
    }

    private async Task<decimal?> GetKnownMaxGradeAsync(
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        IDictionary<long, AssignmentSettingsSummary?> settingsCache,
        CancellationToken cancellationToken)
    {
        if (!settingsCache.TryGetValue(item.AssignmentId, out var settings))
        {
            try
            {
                settings = await settingsGateway.GetAssignmentSettingsAsync(
                    batch.CreatedBySubject,
                    item.CourseId.ToString(CultureInfo.InvariantCulture),
                    item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
            }
            catch
            {
                settings = null;
            }

            settingsCache[item.AssignmentId] = settings;
        }

        if (settings?.MaxGrade > 0)
        {
            return settings.MaxGrade;
        }

        var evidence = await repository.ListEvidenceByItemAsync(item.Id, cancellationToken);
        var maxPoints = evidence
            .Select(item => item.MaxPoints)
            .Where(points => points is > 0)
            .Select(points => points!.Value)
            .ToArray();

        return maxPoints.Length == 0 ? null : maxPoints.Sum();
    }

    private static bool IsReadyForLaunch(AssistedGradingItem item, bool onlyReviewed)
    {
        return (!onlyReviewed || item.ReviewStatus == GradingReviewStatus.Reviewed) &&
            item.Status == GradingItemStatus.ReadyToCommit &&
            item.CommitStatus == GradingCommitStatus.Pending &&
            item.FinalGrade is not null &&
            !string.IsNullOrWhiteSpace(item.FinalFeedback);
    }

    private static IReadOnlyList<string> BuildWarnings(
        int blocked,
        IReadOnlyList<string> scaleWarnings,
        IReadOnlyList<string> contextWarnings)
    {
        var warnings = new List<string>(contextWarnings.Count + scaleWarnings.Count);
        warnings.AddRange(contextWarnings);
        warnings.AddRange(scaleWarnings);
        var otherBlocked = blocked - scaleWarnings.Count - contextWarnings.Count;
        if (otherBlocked > 0)
        {
            warnings.Add($"{otherBlocked} item(ns) bloqueado(s) por falta de revisao, nota final ou feedback final.");
        }

        return warnings;
    }

    private static string FormatGrade(decimal grade)
    {
        return grade.ToString("0.####", CultureInfo.InvariantCulture);
    }


    private static GradingLaunchPayloadItem ToPayloadItem(AssistedGradingItem item)
    {
        return new GradingLaunchPayloadItem(
            item.Id,
            item.CourseId.ToString(CultureInfo.InvariantCulture),
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.FinalGrade ?? 0,
            item.FinalFeedback ?? string.Empty,
            item.AttemptNumber,
            GradingDraftVersionHash.Compute(item),
            item.ContextHash);
    }

    private static GradingLaunchPreviewItem ToPreviewItem(AssistedGradingItem item)
    {
        return new GradingLaunchPreviewItem(
            item.Id,
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.FinalGrade ?? 0,
            item.FinalFeedback ?? string.Empty,
            item.ContextHash);
    }

    private static bool HasVersionedContext(AssistedGradingItem item) =>
        item.ContextVersion is > 0 &&
        !string.IsNullOrWhiteSpace(item.ContextHash) &&
        !string.IsNullOrWhiteSpace(item.ContextStatus) &&
        !string.Equals(item.ContextStatus, "blocked", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(item.ContextStatus, "legacy_unversioned", StringComparison.OrdinalIgnoreCase);
}

public sealed class ConfirmMoodleBatchLaunchCommandHandler(
    IPendingMoodleActionRepository pendingActions,
    IGradingReviewRepository repository,
    IActionConfirmationService confirmations,
    IMoodleGradingCapabilitiesGateway capabilities,
    IMoodleAssignmentGradeReadGateway gradeReadGateway,
    IMoodleAssignmentSubmissionStatusGateway submissionStatusGateway,
    IMoodleParticipantsGateway participantsGateway,
    IMoodleAuditLogRepository auditLogs,
    IMediator mediator)
    : IRequestHandler<ConfirmMoodleBatchLaunchCommand, ConfirmMoodleBatchLaunchResult>
{
    private const string CommitToolName = "confirmar_lancamento_lote_moodle";
    private const string IndividualGradeWriteFunction = "mod_assign_save_grade";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ConfirmMoodleBatchLaunchResult> Handle(
        ConfirmMoodleBatchLaunchCommand request,
        CancellationToken cancellationToken)
    {
        var action = await pendingActions.GetByIdAsync(request.PendingActionId, cancellationToken)
            ?? throw new InvalidOperationException("Acao pendente nao encontrada.");
        var confirmation = await confirmations.ConfirmAsync(
            request.PendingActionId,
            request.ConfirmationText,
            requiredScope: "moodle.write.assignments.grade",
            cancellationToken);
        var payload = JsonSerializer.Deserialize<GradingLaunchPayload>(action.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Payload de lancamento invalido.");
        if (confirmation.Status == "already_confirmed")
        {
            return new ConfirmMoodleBatchLaunchResult(
                "already_confirmed",
                request.PendingActionId,
                SentItems: 0,
                FailedItems: 0,
                Failures: [],
                confirmation.AuditId);
        }
        var sent = 0;
        var executionUnknown = false;
        var failures = new List<GradingLaunchFailure>();
        var userExternalId = action.CreatedByMoodleUserId?.ToString(CultureInfo.InvariantCulture) ??
            action.CreatedBySubject;
        var capabilityFailure = await GetIndividualGradeWriteCapabilityFailureAsync(
            userExternalId,
            cancellationToken);
        if (capabilityFailure is not null)
        {
            await MarkPendingItemsFailedAsync(
                action,
                payload.BatchJobId,
                payload.Items,
                capabilityFailure.Message,
                "commit_blocked",
                capabilityFailure.ErrorCode,
                failures,
                cancellationToken);

            return new ConfirmMoodleBatchLaunchResult(
                confirmation.Status,
                request.PendingActionId,
                SentItems: 0,
                failures.Count,
                failures,
                confirmation.AuditId);
        }

        foreach (var payloadItem in payload.Items)
        {
            var item = await repository.GetItemAsync(payloadItem.GradingItemId, cancellationToken);
            if (item is null)
            {
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, "Item de correcao nao encontrado."));
                continue;
            }

            if (item.CommitStatus == GradingCommitStatus.Succeeded)
            {
                continue;
            }

            if (!HasVersionedContext(item))
            {
                var message = "O contexto versionado da correcao nao esta disponivel. Gere uma nova previa antes de lancar no Moodle.";
                item.MarkCommitFailed(message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new { item.CommitStatus },
                    errorCode: "grading_context_missing",
                    errorMessage: message,
                    cancellationToken);
                continue;
            }

            if (string.IsNullOrWhiteSpace(payloadItem.ContextHash) ||
                !string.Equals(payloadItem.ContextHash, item.ContextHash, StringComparison.Ordinal))
            {
                var message = "O contexto de correcao mudou ou nao foi incluido na previa. Gere uma nova previa antes de lancar no Moodle.";
                item.MarkCommitFailed(message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new
                    {
                        item.CommitStatus,
                        expectedContextHash = payloadItem.ContextHash,
                        currentContextHash = item.ContextHash
                    },
                    errorCode: "grading_context_hash_mismatch",
                    errorMessage: message,
                    cancellationToken);
                continue;
            }

            var currentDraftVersionHash = GradingDraftVersionHash.Compute(item);
            if (!string.Equals(payloadItem.DraftVersionHash, currentDraftVersionHash, StringComparison.Ordinal))
            {
                var message = "O rascunho foi alterado depois da criacao da previa. Gere uma nova previa antes de lancar no Moodle.";
                item.MarkCommitFailed(message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new
                    {
                        item.CommitStatus,
                        expectedDraftVersionHash = payloadItem.DraftVersionHash,
                        currentDraftVersionHash
                    },
                    errorCode: "grading_draft_version_mismatch",
                    errorMessage: message,
                    cancellationToken);
                continue;
            }

            var existingGradeResult = await GetExistingGradeValidationAsync(
                userExternalId,
                payloadItem.AssignmentId,
                payloadItem.StudentId,
                cancellationToken);
            if (existingGradeResult.Failure is not null)
            {
                item.MarkCommitFailed(existingGradeResult.Failure.Message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, existingGradeResult.Failure.Message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new { item.CommitStatus },
                    errorCode: existingGradeResult.Failure.ErrorCode,
                    errorMessage: existingGradeResult.Failure.Message,
                    cancellationToken);
                continue;
            }

            if (existingGradeResult.ExistingGrade?.HasGrade == true && !payload.AllowOverwriteExisting)
            {
                var message = $"O Moodle ja possui nota existente para o estudante {payloadItem.StudentId} na atividade {payloadItem.AssignmentId}. Gere uma confirmacao especifica de sobrescrita antes de lancar.";
                item.MarkCommitFailed(message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new
                    {
                        item.CommitStatus,
                        existingGrade = existingGradeResult.ExistingGrade.Grade
                    },
                    errorCode: "moodle_existing_grade",
                    errorMessage: message,
                    cancellationToken);
                continue;
            }

            var submissionAttemptResult = await GetSubmissionAttemptValidationAsync(
                userExternalId,
                payloadItem,
                cancellationToken);
            if (submissionAttemptResult.Failure is not null)
            {
                item.MarkCommitFailed(submissionAttemptResult.Failure.Message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, submissionAttemptResult.Failure.Message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new
                    {
                        item.CommitStatus,
                        payloadAttemptNumber = payloadItem.AttemptNumber,
                        currentAttemptNumber = submissionAttemptResult.CurrentStatus?.AttemptNumber,
                        currentSubmissionStatus = submissionAttemptResult.CurrentStatus?.SubmissionStatus
                    },
                    errorCode: submissionAttemptResult.Failure.ErrorCode,
                    errorMessage: submissionAttemptResult.Failure.Message,
                    cancellationToken);
                continue;
            }

            if (submissionAttemptResult.CurrentStatus?.HasFeedback == true &&
                !payload.AllowOverwriteExisting &&
                !string.IsNullOrWhiteSpace(payloadItem.FeedbackText))
            {
                var message = $"O Moodle ja possui feedback existente para o estudante {payloadItem.StudentId} na atividade {payloadItem.AssignmentId}. Gere uma confirmacao especifica de sobrescrita antes de lancar.";
                item.MarkCommitFailed(message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new { item.CommitStatus, hasFeedback = true },
                    errorCode: "moodle_existing_feedback",
                    errorMessage: message,
                    cancellationToken);
                continue;
            }

            var enrollmentFailure = await ValidateStudentEnrollmentAsync(
                userExternalId,
                payloadItem.CourseId,
                payloadItem.StudentId,
                cancellationToken);
            if (enrollmentFailure is not null)
            {
                item.MarkCommitFailed(enrollmentFailure.Message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, enrollmentFailure.Message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new { item.CommitStatus },
                    errorCode: enrollmentFailure.ErrorCode,
                    errorMessage: enrollmentFailure.Message,
                    cancellationToken);
                continue;
            }

            try
            {
                var writeResult = await mediator.Send(
                    new SaveAssignmentGradeCommand(
                        userExternalId,
                        payloadItem.AssignmentId,
                        payloadItem.StudentId,
                        payloadItem.Grade,
                        payloadItem.FeedbackText,
                        payloadItem.AttemptNumber ?? -1,
                        AddAttempt: false,
                        ApplyToAll: false,
                        WorkflowState: "graded",
                        CourseId: payloadItem.CourseId),
                    cancellationToken);
                item.MarkCommitSucceeded();
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_succeeded",
                    responseSummary: writeResult,
                    errorCode: null,
                    errorMessage: null,
                    cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var itemExecutionUnknown = MoodleWriteExecutionClassifier.IsUnknown(ex);
                if (itemExecutionUnknown)
                {
                    executionUnknown = true;
                    action.MarkExecutionUnknown();
                    item.MarkCommitExecutionUnknown(ex.Message);
                }
                else
                {
                    item.MarkCommitFailed(ex.Message);
                }
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, ex.Message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    itemExecutionUnknown ? "commit_execution_unknown" : "commit_failed",
                    responseSummary: new { item.CommitStatus, exceptionType = ex.GetType().Name },
                    errorCode: ex is MoodleApiException moodleError ? moodleError.ErrorCode : ex.GetType().Name,
                    errorMessage: ex.Message,
                    cancellationToken);
                if (itemExecutionUnknown)
                {
                    // Once transport leaves the remote result unknown, stop
                    // the batch. Continuing would create additional writes
                    // that cannot be reconciled as one operator decision.
                    break;
                }
            }
        }

        var batch = await repository.GetBatchAsync(payload.BatchJobId, cancellationToken);
        if (batch is not null)
        {
            var allItems = await GradingItemProcessor.LoadAllBatchItemsAsync(
                repository,
                batch.Id,
                cancellationToken);
            GradingItemProcessor.UpdateBatchCounters(batch, allItems);
        }

        await repository.SaveChangesAsync(cancellationToken);
        await pendingActions.SaveChangesAsync(cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        return new ConfirmMoodleBatchLaunchResult(
            executionUnknown ? "execution_unknown" : confirmation.Status,
            request.PendingActionId,
            sent,
            failures.Count,
            failures,
            confirmation.AuditId);
    }

    private async Task<CapabilityValidationFailure?> GetIndividualGradeWriteCapabilityFailureAsync(
        string userExternalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await capabilities.GetFunctionCatalogAsync(userExternalId, cancellationToken);
            return catalog.Functions.Contains(IndividualGradeWriteFunction, StringComparer.OrdinalIgnoreCase)
                ? null
                : new CapabilityValidationFailure(
                    $"A funcao Moodle {IndividualGradeWriteFunction} nao esta disponivel no servico autorizado.",
                    "moodle_function_unavailable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CapabilityValidationFailure(
                $"Nao foi possivel validar a funcao Moodle {IndividualGradeWriteFunction} antes do lancamento: {ex.Message}",
                "moodle_function_validation_failed");
        }
    }

    private static bool HasVersionedContext(AssistedGradingItem item) =>
        item.ContextVersion is > 0 &&
        !string.IsNullOrWhiteSpace(item.ContextHash) &&
        !string.IsNullOrWhiteSpace(item.ContextStatus) &&
        !string.Equals(item.ContextStatus, "blocked", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(item.ContextStatus, "legacy_unversioned", StringComparison.OrdinalIgnoreCase);

    private async Task<ExistingGradeValidationResult> GetExistingGradeValidationAsync(
        string userExternalId,
        string assignmentId,
        string studentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var existingGrade = await gradeReadGateway.GetExistingGradeAsync(
                userExternalId,
                assignmentId,
                studentId,
                cancellationToken);
            return new ExistingGradeValidationResult(existingGrade, Failure: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExistingGradeValidationResult(
                ExistingGrade: null,
                new CapabilityValidationFailure(
                    $"Nao foi possivel validar se ja existe nota no Moodle antes do lancamento: {ex.Message}",
                    "moodle_existing_grade_validation_failed"));
        }
    }

    private async Task<SubmissionAttemptValidationResult> GetSubmissionAttemptValidationAsync(
        string userExternalId,
        GradingLaunchPayloadItem payloadItem,
        CancellationToken cancellationToken)
    {
        if (payloadItem.AttemptNumber is null)
        {
            return new SubmissionAttemptValidationResult(CurrentStatus: null, Failure: null);
        }

        try
        {
            var currentStatus = await submissionStatusGateway.GetSubmissionStatusAsync(
                userExternalId,
                payloadItem.AssignmentId,
                payloadItem.StudentId,
                cancellationToken);
            if (currentStatus?.AttemptNumber is not null &&
                currentStatus.AttemptNumber != payloadItem.AttemptNumber)
            {
                return new SubmissionAttemptValidationResult(
                    currentStatus,
                    new CapabilityValidationFailure(
                        $"A tentativa da submissao mudou no Moodle para o estudante {payloadItem.StudentId} na atividade {payloadItem.AssignmentId}. Gere uma nova previa antes de lancar.",
                        "moodle_submission_attempt_mismatch"));
            }

            if (!string.IsNullOrWhiteSpace(currentStatus?.SubmissionStatus) &&
                !string.Equals(currentStatus.SubmissionStatus, "submitted", StringComparison.OrdinalIgnoreCase))
            {
                return new SubmissionAttemptValidationResult(
                    currentStatus,
                    new CapabilityValidationFailure(
                        $"A submissao atual nao esta entregue no Moodle para o estudante {payloadItem.StudentId} na atividade {payloadItem.AssignmentId}. Gere uma nova previa antes de lancar.",
                        "moodle_submission_not_submitted"));
            }

            return new SubmissionAttemptValidationResult(currentStatus, Failure: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SubmissionAttemptValidationResult(
                CurrentStatus: null,
                new CapabilityValidationFailure(
                    $"Nao foi possivel validar a tentativa atual da submissao no Moodle antes do lancamento: {ex.Message}",
                    "moodle_submission_status_validation_failed"));
        }
    }

    private async Task<CapabilityValidationFailure?> ValidateStudentEnrollmentAsync(
        string userExternalId,
        string courseId,
        string studentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await participantsGateway.GetCourseParticipantsAsync(
                userExternalId,
                courseId,
                ParticipantStatusFilter.All,
                page: 1,
                pageSize: 500,
                studentsOnly: false,
                includeEmail: false,
                groupId: null,
                cancellationToken);

            var found = page.Participants.Any(p =>
                string.Equals(p.UserId, studentId, StringComparison.OrdinalIgnoreCase));

            if (!found && page.HasMore)
            {
                // Se a primeira página não encontrou e há mais, buscar mais páginas
                var currentPage = 2;
                while (!found)
                {
                    var nextPage = await participantsGateway.GetCourseParticipantsAsync(
                        userExternalId,
                        courseId,
                        ParticipantStatusFilter.All,
                        page: currentPage,
                        pageSize: 500,
                        studentsOnly: false,
                        includeEmail: false,
                        groupId: null,
                        cancellationToken);

                    found = nextPage.Participants.Any(p =>
                        string.Equals(p.UserId, studentId, StringComparison.OrdinalIgnoreCase));

                    if (!nextPage.HasMore)
                    {
                        break;
                    }

                    currentPage++;
                }
            }

            return found
                ? null
                : new CapabilityValidationFailure(
                    $"O estudante {studentId} nao esta inscrito no curso {courseId}. Verifique se o estudante ainda pertence a turma antes de lancar a nota.",
                    "moodle_student_not_enrolled");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CapabilityValidationFailure(
                $"Nao foi possivel validar se o estudante {studentId} esta inscrito no curso {courseId} antes do lancamento: {ex.Message}",
                "moodle_enrollment_validation_failed");
        }
    }

    private async Task MarkPendingItemsFailedAsync(
        PendingMoodleAction action,
        Guid batchJobId,
        IReadOnlyList<GradingLaunchPayloadItem> payloadItems,
        string message,
        string status,
        string errorCode,
        List<GradingLaunchFailure> failures,
        CancellationToken cancellationToken)
    {
        foreach (var payloadItem in payloadItems)
        {
            var item = await repository.GetItemAsync(payloadItem.GradingItemId, cancellationToken);
            if (item is null)
            {
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, "Item de correcao nao encontrado."));
                continue;
            }

            if (item.CommitStatus == GradingCommitStatus.Succeeded)
            {
                continue;
            }

            item.MarkCommitFailed(message);
            failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
            await RecordCommitAuditAsync(
                action,
                batchJobId,
                payloadItem,
                status,
                responseSummary: new { item.CommitStatus },
                errorCode,
                message,
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);
    }

    private Task RecordCommitAuditAsync(
        PendingMoodleAction action,
        Guid batchJobId,
        GradingLaunchPayloadItem payloadItem,
        string status,
        object responseSummary,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        return auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = action.CorrelationId,
            BatchJobId = batchJobId,
            ToolName = CommitToolName,
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = action.CreatedBySubject,
            ActorEmail = action.CreatedByEmail,
            ActorMoodleUserId = action.CreatedByMoodleUserId,
            CourseId = action.CourseId,
            MoodleFunction = IndividualGradeWriteFunction,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                batchJobId,
                payloadItem.GradingItemId,
                payloadItem.CourseId,
                payloadItem.AssignmentId,
                payloadItem.StudentId,
                payloadItem.Grade,
                payloadItem.FeedbackText,
                payloadItem.AttemptNumber
            }),
            ResponseSummaryJson = AuditPayloadSanitizer.SerializeSanitized(responseSummary),
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        }, cancellationToken);
    }

    private sealed record ExistingGradeValidationResult(
        AssignmentExistingGrade? ExistingGrade,
        CapabilityValidationFailure? Failure);

    private sealed record SubmissionAttemptValidationResult(
        AssignmentSubmissionAttemptStatus? CurrentStatus,
        CapabilityValidationFailure? Failure);

    private sealed record CapabilityValidationFailure(string Message, string ErrorCode);
}
