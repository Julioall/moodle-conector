using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

public sealed record CreateGradingLaunchPreviewCommand(
    Guid BatchJobId,
    IReadOnlyList<Guid> GradingItemIds,
    bool OnlyReviewed) : IRequest<CreateGradingLaunchPreviewResult>;

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
    [property: JsonPropertyName("feedbackText")] string FeedbackText);

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
    [property: JsonPropertyName("items")] IReadOnlyList<GradingLaunchPayloadItem> Items);

public sealed record GradingLaunchPayloadItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("grade")] decimal Grade,
    [property: JsonPropertyName("feedbackText")] string FeedbackText,
    [property: JsonPropertyName("attemptNumber")] int? AttemptNumber,
    [property: JsonPropertyName("draftVersionHash")] string DraftVersionHash);

public sealed record AssignmentExistingGrade(
    string AssignmentId,
    string StudentId,
    decimal? Grade,
    bool HasGrade);

public sealed class CreateGradingLaunchPreviewCommandHandler(
    IGradingReviewRepository repository,
    IPendingActionService pendingActions,
    ICurrentUserContext currentUser)
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
        var ready = new List<AssistedGradingItem>();
        foreach (var item in launchable)
        {
            var maxGrade = await GetKnownMaxGradeAsync(item.Id, cancellationToken);
            if (maxGrade is not null && item.FinalGrade > maxGrade)
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
                Warnings: scaleWarnings.Count > 0
                    ? scaleWarnings
                    : ["Nenhum item revisado e pronto para lancamento foi encontrado."]);
        }

        var payload = new GradingLaunchPayload(
            batch.Id,
            ready.Select(ToPayloadItem).ToArray());
        var previewItems = ready.Select(ToPreviewItem).ToArray();
        var itemLabel = ready.Count == 1 ? "CORRECAO" : "CORRECOES";
        var activityScope = string.Join(
            ",",
            ready
                .Select(item => item.AssignmentId.ToString(CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal)
                .Take(10));
        var confirmationText =
            $"CONFIRMO O LANCAMENTO DE {ready.Count} {itemLabel} NO MOODLE PARA O LOTE {batch.Id} DO CURSO {batch.CourseId} NAS ATIVIDADES {activityScope} COM ESCOPO NOTA_E_FEEDBACK";
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
            Warnings: BuildWarnings(blocked, scaleWarnings));
    }

    private async Task<IReadOnlyList<AssistedGradingItem>> LoadBatchItemsAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var total = await repository.CountItemsByBatchAsync(batchId, cancellationToken);
        return await repository.ListItemsByBatchAsync(
            batchId,
            page: 1,
            pageSize: Math.Max(1, total),
            cancellationToken);
    }

    private async Task<decimal?> GetKnownMaxGradeAsync(Guid gradingItemId, CancellationToken cancellationToken)
    {
        var evidence = await repository.ListEvidenceByItemAsync(gradingItemId, cancellationToken);
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

    private static IReadOnlyList<string> BuildWarnings(int blocked, IReadOnlyList<string> scaleWarnings)
    {
        var warnings = new List<string>(scaleWarnings);
        var otherBlocked = blocked - scaleWarnings.Count;
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
            GradingDraftVersionHash.Compute(item));
    }

    private static GradingLaunchPreviewItem ToPreviewItem(AssistedGradingItem item)
    {
        return new GradingLaunchPreviewItem(
            item.Id,
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.FinalGrade ?? 0,
            item.FinalFeedback ?? string.Empty);
    }
}

public sealed class ConfirmMoodleBatchLaunchCommandHandler(
    IPendingMoodleActionRepository pendingActions,
    IGradingReviewRepository repository,
    IActionConfirmationService confirmations,
    IMoodleGradingCapabilitiesGateway capabilities,
    IMoodleAssignmentGradeReadGateway gradeReadGateway,
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
            requiredScope: "moodle.write",
            cancellationToken);
        var payload = JsonSerializer.Deserialize<GradingLaunchPayload>(action.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Payload de lancamento invalido.");
        var sent = 0;
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

            if (existingGradeResult.ExistingGrade?.HasGrade == true)
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
                        WorkflowState: "graded"),
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
                item.MarkCommitFailed(ex.Message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, ex.Message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_failed",
                    responseSummary: new { item.CommitStatus, exceptionType = ex.GetType().Name },
                    errorCode: ex.GetType().Name,
                    errorMessage: ex.Message,
                    cancellationToken);
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        return new ConfirmMoodleBatchLaunchResult(
            confirmation.Status,
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

    private sealed record CapabilityValidationFailure(string Message, string ErrorCode);
}
