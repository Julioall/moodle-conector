using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Configuration;
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
    [property: JsonPropertyName("grade")] decimal? Grade,
    [property: JsonPropertyName("feedbackText")] string FeedbackText,
    [property: JsonPropertyName("contextHash")] string? ContextHash = null,
    [property: JsonPropertyName("submissionContentHash")] string? SubmissionContentHash = null,
    [property: JsonPropertyName("studentName")] string? StudentName = null,
    [property: JsonPropertyName("situation")] string Situation = "pronta_para_publicacao",
    [property: JsonPropertyName("preflightHash")] string? PreflightHash = null,
    [property: JsonPropertyName("existingGrade")] decimal? ExistingGrade = null,
    [property: JsonPropertyName("hasExistingFeedback")] bool HasExistingFeedback = false);

public sealed record ConfirmMoodleBatchLaunchCommand(
    Guid PendingActionId,
    string ConfirmationText,
    // Public MCP confirmation only authorizes and leaves the durable worker
    // to execute. The default remains true for direct application callers and
    // legacy tests; the presentation tool passes false explicitly.
    bool ExecuteImmediately = true) : IRequest<ConfirmMoodleBatchLaunchResult>;

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
    [property: JsonPropertyName("allowOverwriteExisting")] bool AllowOverwriteExisting = false,
    [property: JsonPropertyName("publicationId")] Guid? PublicationId = null,
    [property: JsonPropertyName("connectionKey")] string? ConnectionKey = null);

public sealed record GradingLaunchPayloadItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("grade")] decimal? Grade,
    [property: JsonPropertyName("feedbackText")] string FeedbackText,
    [property: JsonPropertyName("attemptNumber")] int? AttemptNumber,
    [property: JsonPropertyName("draftVersionHash")] string DraftVersionHash,
    [property: JsonPropertyName("contextHash")] string? ContextHash = null,
    [property: JsonPropertyName("submissionContentHash")] string? SubmissionContentHash = null,
    [property: JsonPropertyName("preflightHash")] string? PreflightHash = null);

public sealed record AssignmentExistingGrade(
    string AssignmentId,
    string StudentId,
    decimal? Grade,
    bool HasGrade,
    string? Feedback = null,
    decimal? GradeMax = null,
    long? GraderId = null,
    long? TimeModified = null);

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
    IMoodleAssignmentSettingsGateway settingsGateway,
    IMoodleAssignmentGradeReadGateway? gradeReadGateway = null,
    IMoodleAssignmentSubmissionStatusGateway? submissionStatusGateway = null,
    IMoodleParticipantsGateway? participantsGateway = null,
    IMoodleAssignmentSubmissionsGateway? submissionsGateway = null)
    : IRequestHandler<CreateGradingLaunchPreviewCommand, CreateGradingLaunchPreviewResult>
{
    private const string ToolName = "criar_previa_lancamento_lote";
    private static readonly TimeSpan PendingActionExpiration = TimeSpan.FromMinutes(15);

    public async Task<CreateGradingLaunchPreviewResult> Handle(
        CreateGradingLaunchPreviewCommand request,
        CancellationToken cancellationToken)
    {
        var scope = await GradingBatchScopeResolver.ResolveAsync(
            repository,
            currentUser,
            request.BatchJobId,
            cancellationToken);
        var batch = scope.FirstBatch
            ?? throw new InvalidOperationException("A execucao de correcao ainda nao possui sublotes.");
        var destinationRun = scope.DestinationRun;
        if (destinationRun is not null && string.Equals(destinationRun.Destination, "csv", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Esta execucao ja foi direcionada para CSV; gere um novo gradingRunId para publicar no Moodle.");
        }
        var connectionKeys = scope.Batches
            .Select(ResolvePublicationConnectionKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (connectionKeys.Length > 1)
        {
            throw new InvalidOperationException("Uma execucao agregada nao pode misturar conexoes Moodle diferentes; crie uma execucao por conexao.");
        }
        var publicationConnectionKey = connectionKeys[0];
        var allItems = new List<AssistedGradingItem>();
        foreach (var child in scope.Batches)
        {
            allItems.AddRange(await LoadBatchItemsAsync(child.Id, cancellationToken));
        }
        var selectedIds = request.GradingItemIds.ToHashSet();
        var selected = selectedIds.Count == 0
            ? allItems.ToArray()
            : allItems.Where(item => selectedIds.Contains(item.Id)).ToArray();
        var launchable = selected
            .Select(item => ToLaunchCandidate(item, request.OnlyReviewed))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
        var snapshotsByItem = await repository.ListLatestContextSnapshotsByItemsAsync(
            launchable.Select(candidate => candidate.Item.Id).ToArray(),
            cancellationToken);
        // If the live assignment settings endpoint cannot provide a scale,
        // max grade is derived from evidence. Load that evidence once for the
        // whole preview instead of falling back to one SELECT per item.
        var evidenceByItem = await repository.ListEvidenceByItemsAsync(
            launchable.Select(candidate => candidate.Item.Id).ToArray(),
            cancellationToken);
        var restoredContextIdentity = false;
        var scaleWarnings = new List<string>();
        var contextWarnings = new List<string>();
        var ready = new List<GradingLaunchCandidate>();
        var settingsCache = new Dictionary<(long CourseId, long AssignmentId), AssignmentSettingsSummary?>();
        // Enrollment preflight is cached at course scope. The previous
        // implementation cached by (course, student), which still paged the
        // same Moodle participant list once for every student in a 10k run.
        // A course-level set turns that into one paginated read per course.
        var courseEnrollmentCache = new Dictionary<string, (IReadOnlySet<string>? StudentIds, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var existingGradesByTarget = new Dictionary<(long AssignmentId, long StudentId), AssignmentExistingGrade?>();
        var gradeReadFailuresByAssignment = new Dictionary<long, string>();
        if (gradeReadGateway is not null && launchable.Length > 0)
        {
            var gradeReadUserExternalId = batch.CreatedByMoodleUserId?.ToString(CultureInfo.InvariantCulture) ?? batch.CreatedBySubject;
            try
            {
                // Fetch grades in Moodle's bulk endpoint once per assignment
                // set, instead of issuing one remote read for every item in a
                // 10k-item run. The gateway chunks assignments according to
                // the configured Moodle limit and preserves per-assignment
                // errors for safe partial coverage.
                var gradeBatches = await GradingMoodleReadRetry.ExecuteAsync(
                    retryCancellationToken => gradeReadGateway.GetExistingGradesBatchAsync(
                        gradeReadUserExternalId,
                        launchable.Select(candidate => candidate.Item.AssignmentId.ToString(CultureInfo.InvariantCulture)).Distinct().ToArray(),
                        launchable.Select(candidate => candidate.Item.MoodleUserId.ToString(CultureInfo.InvariantCulture)).Distinct().ToArray(),
                        retryCancellationToken),
                    onRetry: null,
                    cancellationToken);
                foreach (var gradeBatch in gradeBatches)
                {
                    if (!long.TryParse(gradeBatch.AssignmentId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var assignmentId))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(gradeBatch.ErrorMessage))
                    {
                        gradeReadFailuresByAssignment[assignmentId] = gradeBatch.ErrorMessage;
                        continue;
                    }

                    foreach (var entry in gradeBatch.Grades)
                    {
                        if (long.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var studentId))
                        {
                            existingGradesByTarget[(assignmentId, studentId)] = entry.Value;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                foreach (var assignmentId in launchable.Select(candidate => candidate.Item.AssignmentId).Distinct())
                {
                    gradeReadFailuresByAssignment[assignmentId] = exception.GetType().Name;
                }
            }
        }
        var bulkSubmissionStatuses = await GradingBulkSubmissionStatusReader.ReadAsync(
            submissionsGateway,
            batch.CreatedByMoodleUserId?.ToString(CultureInfo.InvariantCulture) ?? batch.CreatedBySubject,
            launchable.Select(candidate => candidate.Item.AssignmentId.ToString(CultureInfo.InvariantCulture)).Distinct().ToArray(),
            cancellationToken);
        foreach (var candidate in launchable)
        {
            var preparedCandidate = candidate;
            var item = candidate.Item;
            if (NeedsContextIdentityRestore(item) &&
                snapshotsByItem.TryGetValue(item.Id, out var snapshotDocument))
            {
                try
                {
                    item.RestoreContextSnapshotIdentity(snapshotDocument);
                    restoredContextIdentity = true;
                }
                catch (InvalidOperationException)
                {
                    // A mensagem segura e uniforme abaixo evita expor detalhes
                    // de integridade e mantém o lançamento bloqueado.
                }
            }

            if (!HasVersionedContextIdentity(item))
            {
                contextWarnings.Add(
                    $"Item {item.Id}: contexto de correcao ausente ou legado; gere uma nova previa antes do lancamento.");
                continue;
            }

            if (!HasSealedSubmissionForBlockedContext(item))
            {
                contextWarnings.Add(
                    $"Item {item.Id}: contexto versionado, mas o rascunho nao foi selado com todos os anexos originais da submissao. Gere um novo pacote e salve a proposta incluindo as resourceUris de tipo submission.");
                continue;
            }

            if (candidate.Grade is not null)
            {
                var maxGrade = await GetKnownMaxGradeAsync(
                    batch,
                    item,
                    settingsCache,
                    evidenceByItem,
                    cancellationToken);
                if (maxGrade is null)
                {
                    scaleWarnings.Add(
                        $"Item {item.Id}: nota maxima da atividade nao foi confirmada; lancamento numerico bloqueado.");
                    continue;
                }

                if (candidate.Grade > maxGrade)
                {
                    scaleWarnings.Add(
                        $"Item {item.Id}: nota {FormatGrade(candidate.Grade!.Value)} excede nota maxima {FormatGrade(maxGrade.Value)} identificada pelos criterios.");
                    continue;
                }
            }

            if (gradeReadGateway is not null)
            {
                if (gradeReadFailuresByAssignment.TryGetValue(item.AssignmentId, out var gradeReadFailure))
                {
                    contextWarnings.Add($"Item {item.Id}: nao foi possivel consultar a nota/feedback atual no Moodle ({gradeReadFailure}); a publicacao foi bloqueada.");
                    continue;
                }

                var existingGrade = existingGradesByTarget.GetValueOrDefault((item.AssignmentId, item.MoodleUserId));

                if (existingGrade?.HasGrade == true && !request.AllowOverwriteExisting)
                {
                    contextWarnings.Add($"Item {item.Id}: ja existe nota no Moodle; a publicacao foi bloqueada nesta previa.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(existingGrade?.Feedback) && !request.AllowOverwriteExisting &&
                    !string.IsNullOrWhiteSpace(candidate.FeedbackText))
                {
                    contextWarnings.Add($"Item {item.Id}: ja existe feedback no Moodle; a publicacao foi bloqueada nesta previa.");
                    continue;
                }

                preparedCandidate = candidate with
                {
                    ExistingGrade = existingGrade,
                    PreflightHash = ComputePreflightHash(batch, item, existingGrade)
                };
            }

            // The preview is the operator's complete preflight contract. The
            // confirmation path repeats these reads, but doing them here
            // prevents an obviously stale attempt or enrollment from ever
            // reaching the confirmation screen. Optional dependencies keep
            // legacy/unit-only stores compatible; production registers both.
            if ((submissionStatusGateway is not null || bulkSubmissionStatuses.Attempted) && item.AttemptNumber is not null)
            {
                AssignmentSubmissionAttemptStatus? currentStatus;
                if (bulkSubmissionStatuses.FailedAssignments.Contains(item.AssignmentId.ToString(CultureInfo.InvariantCulture)))
                {
                    contextWarnings.Add($"Item {item.Id}: nao foi possivel validar a tentativa atual no Moodle (falha na consulta em lote); a publicacao foi bloqueada.");
                    continue;
                }
                if (!bulkSubmissionStatuses.TryGet(item.AssignmentId.ToString(CultureInfo.InvariantCulture), item.MoodleUserId.ToString(CultureInfo.InvariantCulture), out currentStatus))
                {
                    if (submissionStatusGateway is null || launchable.Length > GradingBulkSubmissionStatusReader.PerItemFallbackLimit)
                    {
                        contextWarnings.Add($"Item {item.Id}: a submissao atual nao foi encontrada na consulta em lote do Moodle; a publicacao foi bloqueada.");
                        continue;
                    }

                    try
                    {
                        currentStatus = await GradingMoodleReadRetry.ExecuteAsync(
                            retryCancellationToken => submissionStatusGateway.GetSubmissionStatusAsync(
                                batch.CreatedByMoodleUserId?.ToString(CultureInfo.InvariantCulture) ?? batch.CreatedBySubject,
                                item.AssignmentId.ToString(CultureInfo.InvariantCulture),
                                item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
                                retryCancellationToken),
                            onRetry: null,
                            cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        contextWarnings.Add($"Item {item.Id}: nao foi possivel validar a tentativa atual no Moodle ({exception.GetType().Name}); a publicacao foi bloqueada.");
                        continue;
                    }
                }

                if (currentStatus?.AttemptNumber is not null &&
                    currentStatus.AttemptNumber != item.AttemptNumber)
                {
                    contextWarnings.Add($"Item {item.Id}: a tentativa da submissao mudou no Moodle; gere uma nova previa antes do lancamento.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentStatus?.SubmissionStatus) &&
                    !string.Equals(currentStatus.SubmissionStatus, "submitted", StringComparison.OrdinalIgnoreCase))
                {
                    contextWarnings.Add($"Item {item.Id}: a submissao atual nao esta entregue no Moodle; a publicacao foi bloqueada.");
                    continue;
                }

                if (currentStatus?.HasFeedback == true &&
                    !request.AllowOverwriteExisting &&
                    !string.IsNullOrWhiteSpace(candidate.FeedbackText) &&
                    gradeReadGateway is null)
                {
                    contextWarnings.Add($"Item {item.Id}: ja existe feedback no Moodle; a publicacao foi bloqueada nesta previa.");
                    continue;
                }
            }

            if (participantsGateway is not null)
            {
                var courseId = item.CourseId.ToString(CultureInfo.InvariantCulture);
                if (!courseEnrollmentCache.TryGetValue(courseId, out var enrollment))
                {
                    enrollment = await ResolveCourseEnrollmentPreflightAsync(
                        participantsGateway,
                        batch.CreatedByMoodleUserId?.ToString(CultureInfo.InvariantCulture) ?? batch.CreatedBySubject,
                        courseId,
                        cancellationToken);
                    courseEnrollmentCache[courseId] = enrollment;
                }

                if (enrollment.Error is not null)
                {
                    contextWarnings.Add($"Item {item.Id}: nao foi possivel validar a matricula atual no Moodle ({enrollment.Error}); a publicacao foi bloqueada.");
                    continue;
                }

                if (enrollment.StudentIds?.Contains(
                        item.MoodleUserId.ToString(CultureInfo.InvariantCulture)) != true)
                {
                    contextWarnings.Add($"Item {item.Id}: o estudante nao esta matriculado no curso atual do Moodle; a publicacao foi bloqueada.");
                    continue;
                }
            }

            ready.Add(preparedCandidate);
        }

        if (restoredContextIdentity)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }

        var blocked = selected.Length - ready.Count;

        if (ready.Count == 0)
        {
            return new CreateGradingLaunchPreviewResult(
                Guid.Empty,
                request.BatchJobId,
                selected.Length,
                ReadyItems: 0,
                blocked,
                Launches: [],
                ConfirmationText: string.Empty,
                ExpiresAt: null,
                Warnings: scaleWarnings.Count > 0 || contextWarnings.Count > 0
            ? [.. contextWarnings, .. scaleWarnings]
                    : ["Nenhuma correcao salva e pronta para lancamento foi encontrada."]);
        }

        var publicationId = Guid.NewGuid();
        var connectionKey = publicationConnectionKey;
        var duplicateTargetItemIds = ready
            .GroupBy(candidate => (
                candidate.Item.AssignmentId,
                candidate.Item.MoodleUserId,
                AttemptNumber: candidate.Item.AttemptNumber ?? 0))
            .SelectMany(group => group
                .OrderBy(candidate => candidate.Item.Id)
                .Skip(1)
                .Select(candidate => candidate.Item.Id))
            .ToHashSet();
        if (duplicateTargetItemIds.Count > 0)
        {
            contextWarnings.Add($"{duplicateTargetItemIds.Count} item(ns) duplicado(s) para a mesma atividade/aluno/tentativa foram bloqueado(s) nesta previa.");
            ready = ready.Where(candidate => !duplicateTargetItemIds.Contains(candidate.Item.Id)).ToList();
        }

        var claimResults = await repository.TryClaimPublicationTargetsAsync(
            publicationId,
            connectionKey,
            ready.Select(candidate => new GradingPublicationClaimRequest(
                candidate.Item.Id,
                candidate.Item.AssignmentId,
                candidate.Item.MoodleUserId,
                candidate.Item.AttemptNumber ?? 0)).ToArray(),
            DateTimeOffset.UtcNow.Add(PendingActionExpiration),
            cancellationToken);
        var busyItemIds = claimResults
            .Where(result => !result.Claimed)
            .Select(result => result.GradingItemId)
            .ToHashSet();
        if (busyItemIds.Count > 0)
        {
            contextWarnings.Add($"{busyItemIds.Count} item(ns) ja possuem outra publicacao ativa para a mesma entrega/tentativa; foram bloqueado(s) nesta previa.");
            ready = ready.Where(candidate => !busyItemIds.Contains(candidate.Item.Id)).ToList();
        }

        blocked = selected.Length - ready.Count;

        if (ready.Count == 0)
        {
            return new CreateGradingLaunchPreviewResult(
                Guid.Empty,
                request.BatchJobId,
                selected.Length,
                ReadyItems: 0,
                BlockedItems: selected.Length,
                Launches: [],
                ConfirmationText: string.Empty,
                ExpiresAt: null,
                Warnings: [.. contextWarnings, .. scaleWarnings]);
        }

        if (destinationRun is not null)
        {
            // Persist the destination choice atomically so concurrent
            // requests cannot turn one run into both CSV and publication.
            if (!await repository.TrySetGradingRunDestinationAsync(
                    destinationRun.Id,
                    "publish",
                    cancellationToken))
            {
                await repository.ReleasePublicationClaimsAsync(publicationId, CancellationToken.None);
                throw new InvalidOperationException(
                    "Esta execucao foi direcionada para CSV por outra solicitacao; gere um novo gradingRunId para publicar no Moodle.");
            }
        }

        var payload = new GradingLaunchPayload(
            request.BatchJobId,
            ready.Select(ToPayloadItem).ToArray(),
            request.AllowOverwriteExisting,
            publicationId,
            connectionKey);
        var previewItems = ready.Select(ToPreviewItem).ToArray();
        // A previa traz todo o escopo e os avisos; a decisao humana fica em um
        // comando curto e constante, sem exigir que o ChatGPT reproduza IDs.
        var confirmationText = "CONFIRMAR_PUBLICACAO";
        PendingActionResponse pending;
        try
        {
            pending = await pendingActions.CreatePendingActionAsync(
                ToolName,
                ToolRiskLevel.CriticalHumanConfirmedWrite,
                payload,
                new
                {
                    batchJobId = request.BatchJobId,
                    totalItems = selected.Length,
                    readyItems = ready.Count,
                    blockedItems = blocked,
                    launches = previewItems
                },
                confirmationText,
                PendingActionExpiration,
                batch.CourseId,
                cancellationToken);

            // Bind the target mutex to the durable action before returning the
            // preview. If confirmation later succeeds, expiry cleanup can see
            // the Authorized action and will not release these targets during
            // a crash/restart window.
            await repository.BindPublicationClaimsAsync(
                publicationId,
                pending.PendingActionId,
                cancellationToken);
        }
        catch
        {
            await repository.ReleasePublicationClaimsAsync(publicationId, CancellationToken.None);
            throw;
        }

        var warnings = BuildWarnings(blocked, scaleWarnings, contextWarnings).ToList();
        if (request.AllowOverwriteExisting)
        {
            warnings.Add("Esta previa autoriza sobrescrever notas ou feedbacks que ja existam no Moodle.");
        }

        return new CreateGradingLaunchPreviewResult(
            pending.PendingActionId,
            request.BatchJobId,
            selected.Length,
            ready.Count,
            blocked,
            previewItems,
            pending.ConfirmationText,
            pending.ExpiresAt,
            Warnings: warnings);
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
        IDictionary<(long CourseId, long AssignmentId), AssignmentSettingsSummary?> settingsCache,
        IReadOnlyDictionary<Guid, IReadOnlyList<GradingEvidence>> evidenceByItem,
        CancellationToken cancellationToken)
    {
        var settingsKey = (item.CourseId, item.AssignmentId);
        if (!settingsCache.TryGetValue(settingsKey, out var settings))
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

            settingsCache[settingsKey] = settings;
        }

        if (settings?.MaxGrade > 0)
        {
            return settings.MaxGrade;
        }

        var evidence = evidenceByItem.GetValueOrDefault(item.Id, []);
        var maxPoints = evidence
            .Select(item => item.MaxPoints)
            .Where(points => points is > 0)
            .Select(points => points!.Value)
            .ToArray();

        return maxPoints.Length == 0 ? null : maxPoints.Sum();
    }

    private static async Task<(IReadOnlySet<string>? StudentIds, string? Error)> ResolveCourseEnrollmentPreflightAsync(
        IMoodleParticipantsGateway gateway,
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken)
    {
        try
        {
            var studentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pageNumber = 1;
            while (true)
            {
                var page = await GradingMoodleReadRetry.ExecuteAsync(
                    retryCancellationToken => gateway.GetCourseParticipantsAsync(
                        userExternalId,
                        courseId,
                        ParticipantStatusFilter.All,
                        pageNumber,
                        pageSize: 500,
                        studentsOnly: false,
                        includeEmail: false,
                        groupId: null,
                        retryCancellationToken),
                    onRetry: null,
                    cancellationToken);
                foreach (var participant in page.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(participant.UserId))
                    {
                        studentIds.Add(participant.UserId);
                    }
                }

                if (!page.HasMore)
                {
                    return (studentIds, null);
                }

                pageNumber++;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, exception.GetType().Name);
        }
    }

    private static GradingLaunchCandidate? ToLaunchCandidate(AssistedGradingItem item, bool onlyReviewed)
    {
        if (item.ReviewStatus == GradingReviewStatus.Reviewed &&
            item.Status == GradingItemStatus.ReadyToCommit &&
            item.CommitStatus == GradingCommitStatus.Pending &&
            !string.IsNullOrWhiteSpace(item.FinalFeedback))
        {
            return new GradingLaunchCandidate(item, item.FinalGrade, item.FinalFeedback!);
        }

        if (!onlyReviewed &&
            item.Status == GradingItemStatus.DraftReady &&
            item.CommitStatus == GradingCommitStatus.NotReady &&
            !string.IsNullOrWhiteSpace(item.DraftFeedback))
        {
            return new GradingLaunchCandidate(item, item.SuggestedGrade, item.DraftFeedback!);
        }

        return null;
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


    private static GradingLaunchPayloadItem ToPayloadItem(GradingLaunchCandidate candidate)
    {
        var item = candidate.Item;
        return new GradingLaunchPayloadItem(
            item.Id,
            item.CourseId.ToString(CultureInfo.InvariantCulture),
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            candidate.Grade,
            candidate.FeedbackText,
            item.AttemptNumber,
            GradingDraftVersionHash.Compute(item),
            item.ContextHash,
            item.SubmissionContentHash,
            candidate.PreflightHash);
    }

    private static GradingLaunchPreviewItem ToPreviewItem(GradingLaunchCandidate candidate)
    {
        var item = candidate.Item;
        return new GradingLaunchPreviewItem(
            item.Id,
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            candidate.Grade,
            candidate.FeedbackText,
            item.ContextHash,
            item.SubmissionContentHash,
            item.StudentDisplayName,
            candidate.Item.ReviewStatus == GradingReviewStatus.Reviewed
                ? "revisada"
                : "rascunho_aguardando_confirmacao",
            candidate.PreflightHash,
            candidate.ExistingGrade?.Grade,
            !string.IsNullOrWhiteSpace(candidate.ExistingGrade?.Feedback));
    }

    private static string ResolvePublicationConnectionKey(AssistedGradingBatch batch) =>
        !string.IsNullOrWhiteSpace(batch.MoodleConnectionId)
            ? $"connection:{batch.MoodleConnectionId.Trim()}"
            : $"client:{(string.IsNullOrWhiteSpace(batch.ConnectorClientId) ? "default" : batch.ConnectorClientId)}" +
              $":alias:{(string.IsNullOrWhiteSpace(batch.ConnectionAlias) ? "default" : batch.ConnectionAlias)}";

    private sealed record GradingLaunchCandidate(
        AssistedGradingItem Item,
        decimal? Grade,
        string FeedbackText,
        AssignmentExistingGrade? ExistingGrade = null,
        string? PreflightHash = null);

    private static string ComputePreflightHash(
        AssistedGradingBatch batch,
        AssistedGradingItem item,
        AssignmentExistingGrade? existingGrade)
    {
        var canonical = string.Join("|",
            ResolvePublicationConnectionKey(batch),
            item.AssignmentId,
            item.MoodleUserId,
            item.AttemptNumber ?? 0,
            existingGrade?.HasGrade == true
                ? existingGrade.Grade?.ToString("0.####", CultureInfo.InvariantCulture) ?? "null"
                : "none",
            NormalizePreflightFeedback(existingGrade?.Feedback),
            !string.IsNullOrWhiteSpace(existingGrade?.Feedback) ? "feedback" : "no-feedback",
            item.SubmissionContentHash ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string NormalizePreflightFeedback(string? value) =>
        string.Join(" ", (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool HasVersionedContextIdentity(AssistedGradingItem item) =>
        item.ContextVersion is > 0 &&
        !string.IsNullOrWhiteSpace(item.ContextHash) &&
        !string.IsNullOrWhiteSpace(item.ContextStatus) &&
        !string.Equals(item.ContextStatus, "legacy_unversioned", StringComparison.OrdinalIgnoreCase);

    private static bool HasSealedSubmissionForBlockedContext(AssistedGradingItem item) =>
        !string.Equals(item.ContextStatus, "blocked", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(item.SubmissionContentHash);

    private static bool NeedsContextIdentityRestore(AssistedGradingItem item) =>
        item.ContextVersion is null or <= 0 ||
        string.IsNullOrWhiteSpace(item.ContextHash) ||
        string.IsNullOrWhiteSpace(item.ContextStatus) ||
        string.Equals(item.ContextStatus, "legacy_unversioned", StringComparison.OrdinalIgnoreCase);
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
    IMediator mediator,
    IMoodleResourceRepository? resourceRepository = null,
    ISubmissionContentHashResolver? submissionContentHashResolver = null,
    IOptions<MoodleUniversalApiFeatureOptions>? resourceFeatures = null,
    IMoodleConnectorCredentialsProvider? credentialsProvider = null,
    IMoodleAssignmentSubmissionsGateway? submissionsGateway = null)
    : IRequestHandler<ConfirmMoodleBatchLaunchCommand, ConfirmMoodleBatchLaunchResult>
{
    private const string CommitToolName = "confirmar_lancamento_lote_moodle";
    private const string IndividualGradeWriteFunction = "mod_assign_save_grade";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ExecutionLeaseRenewalInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PublicationClaimRefreshDuration = TimeSpan.FromMinutes(15);
    private const int DurableCheckpointInterval = 25;

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

        var durableExecution = confirmation.Status == "authorized";
        // Preserve the connection binding even though the public confirmation
        // request does not execute writes itself. This rejects a confirmation
        // sent through a different Moodle connection/alias than the preview.
        await EnsurePreviewConnectionAsync(payload, credentialsProvider, cancellationToken);
        if (durableExecution && !request.ExecuteImmediately)
        {
            // Keep the confirmation request short even for a 10k-item
            // publication. The action is already Authorized durably and the
            // publication worker will claim it on the next poll (or recover
            // it after a process restart). No Moodle write happens here.
            return new ConfirmMoodleBatchLaunchResult(
                "authorized",
                request.PendingActionId,
                SentItems: 0,
                FailedItems: 0,
                Failures: [],
                confirmation.AuditId);
        }
        GradingRun? gradingRun = null;
        var publicationClaimConflicts = new HashSet<Guid>();
        var executionOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        if (durableExecution)
        {
            // Reassert the target claims before taking the worker lease. This
            // is a no-op for the original preview (the same publication owns
            // those rows), and it safely reacquires only free targets when a
            // PartiallyCompleted action is retried after its claims were
            // released. A target stolen by another publication is reported as
            // a per-item conflict and is never written without a claim.
            if (payload.PublicationId is Guid publicationId)
            {
                var claimResults = await repository.TryClaimPublicationTargetsAsync(
                    publicationId,
                    payload.ConnectionKey ?? "default",
                    payload.Items.Select(item => new GradingPublicationClaimRequest(
                        item.GradingItemId,
                        long.TryParse(item.AssignmentId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var assignmentId)
                            ? assignmentId
                            : 0,
                        long.TryParse(item.StudentId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var moodleUserId)
                            ? moodleUserId
                            : 0,
                        item.AttemptNumber ?? 0)).ToArray(),
                    DateTimeOffset.UtcNow.Add(PublicationClaimRefreshDuration),
                    cancellationToken);
                publicationClaimConflicts.UnionWith(
                    claimResults
                        .Where(result => !result.Claimed)
                        .Select(result => result.GradingItemId));
                await repository.ActivatePublicationClaimsAsync(publicationId, cancellationToken);
            }

            var executionClaim = await pendingActions.TryBeginExecutionAsync(
                request.PendingActionId,
                executionOwner,
                DateTimeOffset.UtcNow,
                ExecutionLeaseDuration,
                cancellationToken);
            if (!executionClaim.Claimed)
            {
                if (executionClaim.Status is PendingActionStatus.Executed or PendingActionStatus.Failed)
                {
                    if (payload.PublicationId is Guid completedPublicationId)
                    {
                        await repository.ReleasePublicationClaimsAsync(completedPublicationId, cancellationToken);
                    }

                    return new ConfirmMoodleBatchLaunchResult(
                        executionClaim.Status == PendingActionStatus.Executed ? "already_executed" : "already_failed",
                        request.PendingActionId,
                        SentItems: 0,
                        FailedItems: 0,
                        Failures: [],
                        confirmation.AuditId);
                }

                return new ConfirmMoodleBatchLaunchResult(
                    "executing",
                    request.PendingActionId,
                    SentItems: 0,
                    FailedItems: 0,
                    Failures: [],
                    confirmation.AuditId);
            }

            gradingRun = await ResolvePublicationRunAsync(payload.BatchJobId, cancellationToken);
            if (gradingRun is not null)
            {
                gradingRun.MarkPublishing();
                await repository.SaveChangesAsync(cancellationToken);
            }

            // The repository refreshes its tracked instance after the atomic
            // claim. Reloading here also makes InMemory and PostgreSQL paths
            // observe the same durable execution state.
            action = await pendingActions.GetByIdAsync(request.PendingActionId, cancellationToken)
                ?? throw new InvalidOperationException("Acao pendente desapareceu durante a execucao.");
        }
        var sent = 0;
        var executionUnknown = false;
        var executionLeaseLost = false;
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

            if (durableExecution)
            {
                action.MarkFailed(capabilityFailure.Message);
                await pendingActions.SaveChangesAsync(cancellationToken);
                if (payload.PublicationId is Guid publicationId)
                {
                    await repository.ReleasePublicationClaimsAsync(publicationId, cancellationToken);
                }
                if (gradingRun is not null)
                {
                    gradingRun.MarkFailed();
                    await repository.SaveChangesAsync(cancellationToken);
                }
            }

            return new ConfirmMoodleBatchLaunchResult(
                durableExecution ? "partial_failure" : confirmation.Status,
                request.PendingActionId,
                SentItems: 0,
                failures.Count,
                failures,
                confirmation.AuditId);
        }

        // Revalidate the whole publication's current grades in one bulk read
        // before entering the per-item write loop. The concrete Moodle
        // gateway uses mod_assign_get_grades and chunks assignment IDs; the
        // interface fallback remains bounded for legacy/test gateways.
        var existingGradesByTarget = new Dictionary<(string AssignmentId, string StudentId), AssignmentExistingGrade?>();
        var existingGradeFailuresByAssignment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var publicationStudentIds = payload.Items
            .Select(item => item.StudentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        try
        {
            var gradeBatches = await GradingMoodleReadRetry.ExecuteAsync(
                retryCancellationToken => gradeReadGateway.GetExistingGradesBatchAsync(
                    userExternalId,
                    payload.Items.Select(item => item.AssignmentId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    publicationStudentIds,
                    retryCancellationToken),
                onRetry: null,
                cancellationToken);
            foreach (var gradeBatch in gradeBatches)
            {
                if (!string.IsNullOrWhiteSpace(gradeBatch.ErrorMessage))
                {
                    try
                    {
                        // The bulk gateway can report an assignment-local
                        // failure while the rest of the request succeeded.
                        // Retry that assignment once through its single-
                        // assignment path so a transient response does not
                        // block an otherwise valid 10k publication.
                        var fallbackGrades = await GradingMoodleReadRetry.ExecuteAsync(
                            retryCancellationToken => gradeReadGateway.GetExistingGradesAsync(
                                userExternalId,
                                gradeBatch.AssignmentId,
                                publicationStudentIds,
                                retryCancellationToken),
                            onRetry: null,
                            cancellationToken);
                        foreach (var entry in fallbackGrades)
                        {
                            existingGradesByTarget[(gradeBatch.AssignmentId, entry.Key)] = entry.Value;
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        existingGradeFailuresByAssignment[gradeBatch.AssignmentId] = exception.GetType().Name;
                    }
                    continue;
                }

                foreach (var entry in gradeBatch.Grades)
                {
                    existingGradesByTarget[(gradeBatch.AssignmentId, entry.Key)] = entry.Value;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            foreach (var assignmentId in payload.Items.Select(item => item.AssignmentId).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                existingGradeFailuresByAssignment[assignmentId] = exception.GetType().Name;
            }
        }

        // Enrollment is likewise cached by course. A run with thousands of
        // students must not walk the same paginated participant list once per
        // item during confirmation.
        var courseEnrollmentCache = new Dictionary<string, (IReadOnlySet<string>? StudentIds, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var submissionStatusCache = new Dictionary<(string AssignmentId, string StudentId), SubmissionAttemptValidationResult>();
        var bulkSubmissionStatuses = await GradingBulkSubmissionStatusReader.ReadAsync(
            submissionsGateway,
            userExternalId,
            payload.Items.Select(item => item.AssignmentId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cancellationToken);
        // Hydrate the tracked grading items in one indexed query. The write
        // itself remains per item (Moodle's save-grade contract is per
        // student), but confirmation must not add one database round trip per
        // item to a 10k publication.
        var itemsById = await repository.GetItemsAsync(
            payload.Items.Select(item => item.GradingItemId).Distinct().ToArray(),
            cancellationToken);
        var resourcesById = new Dictionary<string, MoodleResource>(StringComparer.Ordinal);
        if (resourceRepository is not null && itemsById.Count > 0)
        {
            var submissionResourceIds = itemsById.Values
                .SelectMany(item => item.GetSubmissionResourceIds())
                .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (submissionResourceIds.Length > 0)
            {
                foreach (var entry in await resourceRepository.FindManyAsync(submissionResourceIds, cancellationToken))
                {
                    resourcesById[entry.Key] = entry.Value;
                }
            }
        }

        var itemsSinceCheckpoint = 0;
        var lastLeaseRenewalAt = DateTimeOffset.UtcNow;
        foreach (var payloadItem in payload.Items)
        {
            try
            {
                var leaseNow = DateTimeOffset.UtcNow;
                if (durableExecution && leaseNow - lastLeaseRenewalAt >= ExecutionLeaseRenewalInterval &&
                    !await pendingActions.TryRenewExecutionLeaseAsync(
                        request.PendingActionId,
                        executionOwner,
                        leaseNow,
                        ExecutionLeaseDuration,
                        cancellationToken))
                {
                    // Another worker has recovered an expired lease. Preserve
                    // its state instead of writing a terminal status from this
                    // stale request; the other worker now owns the remaining
                    // items.
                    executionLeaseLost = true;
                    break;
                }
                if (durableExecution && leaseNow - lastLeaseRenewalAt >= ExecutionLeaseRenewalInterval)
                {
                    lastLeaseRenewalAt = leaseNow;
                }

                if (!itemsById.TryGetValue(payloadItem.GradingItemId, out var item))
                {
                    failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, "Item de correcao nao encontrado."));
                    continue;
                }

                if (publicationClaimConflicts.Contains(payloadItem.GradingItemId))
                {
                    const string message = "Outra publicacao assumiu esta atividade/aluno/tentativa antes da retomada; o item nao foi enviado.";
                    failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
                    await RecordCommitAuditAsync(
                        action,
                        payload.BatchJobId,
                        payloadItem,
                        "commit_blocked",
                        responseSummary: new { item.CommitStatus },
                        errorCode: "publication_target_busy",
                        errorMessage: message,
                        cancellationToken);
                    continue;
                }

            if (item.CommitStatus == GradingCommitStatus.Succeeded)
            {
                continue;
            }

            if (!HasVersionedContextIdentity(item) || !HasSealedSubmissionForBlockedContext(item))
            {
                var message = HasVersionedContextIdentity(item)
                    ? "O rascunho nao foi selado com os anexos originais da submissao. Gere um novo pacote e uma nova previa antes de lancar no Moodle."
                    : "O contexto versionado da correcao nao esta disponivel. Gere uma nova previa antes de lancar no Moodle.";
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

            var integrityFailure = await ValidateSubmissionIntegrityAsync(
                userExternalId,
                item,
                payloadItem,
                resourcesById,
                cancellationToken);
            if (integrityFailure is not null)
            {
                item.MarkCommitFailed(integrityFailure.Message);
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, integrityFailure.Message));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    "commit_blocked",
                    responseSummary: new { item.CommitStatus, item.SubmissionContentHash },
                    errorCode: integrityFailure.ErrorCode,
                    errorMessage: integrityFailure.Message,
                    cancellationToken);
                continue;
            }

            if (payloadItem.Grade is not null || payloadItem.PreflightHash is not null)
            {
                ExistingGradeValidationResult existingGradeResult;
                if (existingGradeFailuresByAssignment.TryGetValue(payloadItem.AssignmentId, out var gradeReadFailure))
                {
                    existingGradeResult = new(
                        ExistingGrade: null,
                        new CapabilityValidationFailure(
                            $"Nao foi possivel validar se ja existe nota no Moodle antes do lancamento: {gradeReadFailure}",
                            "moodle_existing_grade_validation_failed"));
                }
                else
                {
                    existingGradesByTarget.TryGetValue(
                        (payloadItem.AssignmentId, payloadItem.StudentId),
                        out var existingGrade);
                    existingGradeResult = new(existingGrade, Failure: null);
                }
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

                if (!string.IsNullOrWhiteSpace(payloadItem.PreflightHash))
                {
                    var currentPreflightHash = ComputePreflightHash(
                        payload.ConnectionKey ?? "default",
                        payloadItem,
                        existingGradeResult.ExistingGrade);
                    if (!string.Equals(payloadItem.PreflightHash, currentPreflightHash, StringComparison.Ordinal) &&
                        PublishedValuesMatch(payloadItem, existingGradeResult.ExistingGrade))
                    {
                        // A worker may have reached Moodle and lost the
                        // response before its item checkpoint was persisted.
                        // If the observed remote state already equals the
                        // requested values, reconcile the item locally instead
                        // of issuing a duplicate write after restart.
                        item.MarkCommitSucceeded();
                        sent++;
                        await RecordCommitAuditAsync(
                            action,
                            payload.BatchJobId,
                            payloadItem,
                            "commit_recovered_succeeded",
                            responseSummary: new { item.CommitStatus, currentPreflightHash },
                            errorCode: "moodle_write_recovered_from_remote_state",
                            errorMessage: null,
                            cancellationToken);
                        continue;
                    }

                    if (!string.Equals(payloadItem.PreflightHash, currentPreflightHash, StringComparison.Ordinal))
                    {
                        var message = "O estado atual da nota/feedback mudou desde a previa. Gere uma nova previa antes de lancar no Moodle.";
                        item.MarkCommitFailed(message);
                        failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, message));
                        await RecordCommitAuditAsync(
                            action,
                            payload.BatchJobId,
                            payloadItem,
                            "commit_blocked",
                            responseSummary: new { item.CommitStatus, expectedPreflightHash = payloadItem.PreflightHash, currentPreflightHash },
                            errorCode: "moodle_preflight_hash_mismatch",
                            errorMessage: message,
                            cancellationToken);
                        continue;
                    }
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
            }

            var statusKey = (payloadItem.AssignmentId, payloadItem.StudentId);
            if (!submissionStatusCache.TryGetValue(statusKey, out var submissionAttemptResult))
            {
                if (payloadItem.AttemptNumber is null)
                {
                    submissionAttemptResult = new SubmissionAttemptValidationResult(CurrentStatus: null, Failure: null);
                }
                else if (bulkSubmissionStatuses.FailedAssignments.Contains(payloadItem.AssignmentId))
                {
                    submissionAttemptResult = new SubmissionAttemptValidationResult(
                        CurrentStatus: null,
                        new CapabilityValidationFailure(
                            $"Nao foi possivel validar a tentativa atual da submissao no Moodle para a atividade {payloadItem.AssignmentId}.",
                            "moodle_submission_status_validation_failed"));
                }
                else if (bulkSubmissionStatuses.TryGet(payloadItem.AssignmentId, payloadItem.StudentId, out var bulkStatus))
                {
                    submissionAttemptResult = ValidateSubmissionAttempt(payloadItem, bulkStatus);
                }
                else if (submissionsGateway is not null || payload.Items.Count > GradingBulkSubmissionStatusReader.PerItemFallbackLimit)
                {
                    submissionAttemptResult = new SubmissionAttemptValidationResult(
                        CurrentStatus: null,
                        new CapabilityValidationFailure(
                            $"A submissao atual nao foi encontrada na consulta em lote do Moodle para o estudante {payloadItem.StudentId}.",
                            "moodle_submission_not_found"));
                }
                else
                {
                    submissionAttemptResult = await GetSubmissionAttemptValidationAsync(
                        userExternalId,
                        payloadItem,
                        cancellationToken);
                }
                submissionStatusCache[statusKey] = submissionAttemptResult;
            }
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

            if (!courseEnrollmentCache.TryGetValue(payloadItem.CourseId, out var courseEnrollment))
            {
                courseEnrollment = await ResolveCourseEnrollmentAsync(
                    userExternalId,
                    payloadItem.CourseId,
                    cancellationToken);
                courseEnrollmentCache[payloadItem.CourseId] = courseEnrollment;
            }

            var enrollmentFailure = courseEnrollment.Error is not null
                ? new CapabilityValidationFailure(
                    $"Nao foi possivel validar se o estudante {payloadItem.StudentId} esta inscrito no curso {payloadItem.CourseId} antes do lancamento: {courseEnrollment.Error}",
                    "moodle_enrollment_validation_failed")
                : courseEnrollment.StudentIds?.Contains(payloadItem.StudentId) == true
                    ? null
                    : new CapabilityValidationFailure(
                        $"O estudante {payloadItem.StudentId} nao esta inscrito no curso {payloadItem.CourseId}. Verifique se o estudante ainda pertence a turma antes de lancar a nota.",
                        "moodle_student_not_enrolled");
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
                // Rascunhos da IA permanecem apenas internos ate a confirmacao
                // explicita. A confirmacao e o ato de revisao/aprovacao que os
                // promove para o mesmo estado usado pelo caminho em lote legado.
                if (item.ReviewStatus != GradingReviewStatus.Reviewed)
                {
                    item.ApplyTeacherReview(
                        payloadItem.Grade,
                        payloadItem.FeedbackText,
                        action.CreatedBySubject,
                        action.CreatedByMoodleUserId,
                        teacherDecision: "confirmed_for_publication");
                }

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
                    var reconciliation = await ReconcileUnknownWriteAsync(
                        userExternalId,
                        payloadItem,
                        cancellationToken);

                    if (reconciliation.Status == UnknownWriteReconciliationStatus.Applied)
                    {
                        item.MarkCommitSucceeded();
                        sent++;
                        await RecordCommitAuditAsync(
                            action,
                            payload.BatchJobId,
                            payloadItem,
                            "commit_reconciled_succeeded",
                            responseSummary: new { item.CommitStatus, reconciliation.Message },
                            errorCode: "moodle_write_reconciled_applied",
                            errorMessage: null,
                            cancellationToken);
                        continue;
                    }

                    if (reconciliation.Status == UnknownWriteReconciliationStatus.NotApplied)
                    {
                        item.MarkCommitFailed(reconciliation.Message);
                        failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, reconciliation.Message));
                        await RecordCommitAuditAsync(
                            action,
                            payload.BatchJobId,
                            payloadItem,
                            "commit_reconciled_not_applied",
                            responseSummary: new { item.CommitStatus, reconciliation.Message },
                            errorCode: "moodle_write_reconciled_not_applied",
                            errorMessage: reconciliation.Message,
                            cancellationToken);
                        continue;
                    }

                    executionUnknown = true;
                    action.MarkExecutionUnknown(reconciliation.Message);
                    item.MarkCommitExecutionUnknown(reconciliation.Message);
                }
                else
                {
                    item.MarkCommitFailed(ex.Message);
                }
                var failureMessage = itemExecutionUnknown
                    ? item.CommitError ?? ex.Message
                    : ex.Message;
                failures.Add(new GradingLaunchFailure(payloadItem.GradingItemId, failureMessage));
                await RecordCommitAuditAsync(
                    action,
                    payload.BatchJobId,
                    payloadItem,
                    itemExecutionUnknown ? "commit_execution_unknown" : "commit_failed",
                    responseSummary: new { item.CommitStatus, exceptionType = ex.GetType().Name },
                    errorCode: ex is MoodleApiException moodleError ? moodleError.ErrorCode : ex.GetType().Name,
                    errorMessage: failureMessage,
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
            finally
            {
                // A publication is a durable workflow. Checkpointing in
                // small chunks keeps restart recovery bounded while avoiding
                // one database flush per item in a 10k payload.
                if (durableExecution && !executionLeaseLost)
                {
                    itemsSinceCheckpoint++;
                    // Checkpoint in small durable chunks. The per-item
                    // statuses remain idempotent and the preflight read can
                    // reconcile a remote write after a crash, while 10k
                    // publications no longer require 30k database flushes.
                    var checkpoint = itemsSinceCheckpoint >= DurableCheckpointInterval ||
                        executionUnknown || executionLeaseLost ||
                        payloadItem == payload.Items[^1];
                    if (checkpoint)
                    {
                        await repository.SaveChangesAsync(cancellationToken);
                        await pendingActions.SaveChangesAsync(cancellationToken);
                        await auditLogs.SaveChangesAsync(cancellationToken);
                        itemsSinceCheckpoint = 0;
                    }
                }
            }
        }

        // Do not flush tracked entities after the durable execution lease was
        // lost. Another worker may already own the action; saving this stale
        // DbContext could overwrite its item ledger or terminal action state.
        if (durableExecution && executionLeaseLost)
        {
            return new ConfirmMoodleBatchLaunchResult(
                "executing",
                request.PendingActionId,
                sent,
                failures.Count,
                failures,
                confirmation.AuditId);
        }

        var directBatch = await repository.GetBatchAsync(payload.BatchJobId, cancellationToken);
        if (directBatch is not null)
        {
            var allItems = await GradingItemProcessor.LoadAllBatchItemsAsync(
                repository,
                directBatch.Id,
                cancellationToken);
            GradingItemProcessor.UpdateBatchCounters(directBatch, allItems);
        }
        else
        {
            var run = await repository.GetGradingRunAsync(payload.BatchJobId, cancellationToken);
            if (run is not null)
            {
                var childBatches = await repository.ListBatchesByGradingRunAsync(run.Id, cancellationToken);
                foreach (var child in childBatches)
                {
                    var allItems = await GradingItemProcessor.LoadAllBatchItemsAsync(repository, child.Id, cancellationToken);
                    GradingItemProcessor.UpdateBatchCounters(child, allItems);
                }
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        await pendingActions.SaveChangesAsync(cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        if (durableExecution && !executionLeaseLost)
        {
            if (executionUnknown)
            {
                action.MarkExecutionUnknown();
            }
            else if (failures.Count == 0)
            {
                action.MarkExecuted();
            }
            else
            {
                action.MarkPartiallyCompleted($"{failures.Count} item(ns) nao foram publicados.");
            }

            await pendingActions.SaveChangesAsync(cancellationToken);
            // Definite per-item failures are terminal for this attempt and
            // must not leave a perpetual target mutex. An action in
            // PartiallyCompleted can still be retried manually; successful
            // items are skipped by CommitStatus and only unresolved items are
            // attempted again. Unknown transport outcomes remain locked until
            // explicit reconciliation.
            if (!executionUnknown && payload.PublicationId is Guid publicationId)
            {
                await repository.ReleasePublicationClaimsAsync(publicationId, cancellationToken);
            }

            if (gradingRun is not null)
            {
                if (executionUnknown)
                {
                    gradingRun.MarkPartiallyCompleted();
                }
                else if (failures.Count == 0)
                {
                    // A preview may intentionally contain only a selected
                    // page/subset of a run. Do not report the aggregate as
                    // completed while other children still have work (for
                    // example, items waiting for AI or human review).
                    if (await HasUnpublishedRunItemsAsync(gradingRun, cancellationToken))
                    {
                        gradingRun.MarkPartiallyCompleted();
                    }
                    else
                    {
                        gradingRun.MarkCompleted();
                    }
                }
                else
                {
                    gradingRun.MarkPartiallyCompleted();
                }
                await repository.SaveChangesAsync(cancellationToken);
            }
        }

        return new ConfirmMoodleBatchLaunchResult(
            executionLeaseLost
                ? "executing"
                : executionUnknown
                ? "execution_unknown"
                : durableExecution
                    ? failures.Count == 0 ? "executed" : "partial_failure"
                    : confirmation.Status,
            request.PendingActionId,
            sent,
            failures.Count,
            failures,
            confirmation.AuditId);
    }

    private static async Task EnsurePreviewConnectionAsync(
        GradingLaunchPayload payload,
        IMoodleConnectorCredentialsProvider? credentialsProvider,
        CancellationToken cancellationToken)
    {
        if (credentialsProvider is null || string.IsNullOrWhiteSpace(payload.ConnectionKey))
        {
            return;
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var expected = payload.ConnectionKey.Trim();
        var actual = expected.StartsWith("connection:", StringComparison.Ordinal)
            ? $"connection:{credentials.ConnectionId}"
            : $"client:{credentials.ClientId}:alias:{credentials.Alias}";
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A conexao Moodle selecionada mudou desde a previa. Selecione a conexao original e gere uma nova previa antes de publicar.");
        }
    }

    private async Task<CapabilityValidationFailure?> GetIndividualGradeWriteCapabilityFailureAsync(
        string userExternalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await GradingMoodleReadRetry.ExecuteAsync(
                retryCancellationToken => capabilities.GetFunctionCatalogAsync(
                    userExternalId,
                    retryCancellationToken),
                onRetry: null,
                cancellationToken);
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

    private static bool HasVersionedContextIdentity(AssistedGradingItem item) =>
        item.ContextVersion is > 0 &&
        !string.IsNullOrWhiteSpace(item.ContextHash) &&
        !string.IsNullOrWhiteSpace(item.ContextStatus) &&
        !string.Equals(item.ContextStatus, "legacy_unversioned", StringComparison.OrdinalIgnoreCase);

    private static bool HasSealedSubmissionForBlockedContext(AssistedGradingItem item) =>
        !string.Equals(item.ContextStatus, "blocked", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(item.SubmissionContentHash);

    private static string ComputePreflightHash(
        string connectionKey,
        GradingLaunchPayloadItem payloadItem,
        AssignmentExistingGrade? existingGrade)
    {
        var canonical = string.Join("|",
            connectionKey,
            payloadItem.AssignmentId,
            payloadItem.StudentId,
            payloadItem.AttemptNumber ?? 0,
            existingGrade?.HasGrade == true
                ? existingGrade.Grade?.ToString("0.####", CultureInfo.InvariantCulture) ?? "null"
                : "none",
            NormalizeFeedback(existingGrade?.Feedback),
            !string.IsNullOrWhiteSpace(existingGrade?.Feedback) ? "feedback" : "no-feedback",
            payloadItem.SubmissionContentHash ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private async Task<GradingRun?> ResolvePublicationRunAsync(
        Guid requestedId,
        CancellationToken cancellationToken)
    {
        var directBatch = await repository.GetBatchAsync(requestedId, cancellationToken);
        if (directBatch?.GradingRunId is Guid runId)
        {
            return await repository.GetGradingRunAsync(runId, cancellationToken);
        }

        return await repository.GetGradingRunAsync(requestedId, cancellationToken);
    }

    private async Task<bool> HasUnpublishedRunItemsAsync(
        GradingRun run,
        CancellationToken cancellationToken)
    {
        var childBatches = await repository.ListBatchesByGradingRunAsync(run.Id, cancellationToken);
        foreach (var child in childBatches)
        {
            var childItems = await GradingItemProcessor.LoadAllBatchItemsAsync(
                repository,
                child.Id,
                cancellationToken);
            if (childItems.Any(item =>
                    item.CommitStatus != GradingCommitStatus.Succeeded &&
                    item.Status is not (GradingItemStatus.Blocked or GradingItemStatus.Failed)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PublishedValuesMatch(
        GradingLaunchPayloadItem payloadItem,
        AssignmentExistingGrade? existingGrade)
    {
        var gradeMatches = payloadItem.Grade is null
            ? existingGrade?.HasGrade != true
            : existingGrade?.HasGrade == true && existingGrade.Grade == payloadItem.Grade;
        return gradeMatches && FeedbackMatches(existingGrade?.Feedback, payloadItem.FeedbackText);
    }

    private async Task<CapabilityValidationFailure?> ValidateSubmissionIntegrityAsync(
        string userExternalId,
        AssistedGradingItem item,
        GradingLaunchPayloadItem payloadItem,
        IReadOnlyDictionary<string, MoodleResource> resourcesById,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.SubmissionContentHash))
        {
            // Lotes legados não possuem um hash de submissão. O caminho MCP
            // sempre o sela no draft e, portanto, não alcança esta exceção.
            return null;
        }

        if (resourceFeatures?.Value.McpGradingWriteEnabled != true)
        {
            return new CapabilityValidationFailure(
                "O lancamento Moodle para drafts MCP esta desabilitado pela configuracao de rollout.",
                "mcp_grading_write_disabled");
        }

        if (item.SubmissionId is not long submissionId ||
            resourceRepository is null ||
            submissionContentHashResolver is null)
        {
            return new CapabilityValidationFailure(
                "A integridade da submissao revisada nao pode ser revalidada antes do lancamento.",
                "submission_integrity_validation_unavailable");
        }

        try
        {
            var hashes = new List<string>();
            foreach (var resourceId in item.GetSubmissionResourceIds())
            {
                resourcesById.TryGetValue(resourceId, out var resource);
                if (resource is null || resource.IsExpired(DateTimeOffset.UtcNow) ||
                    resource.SubmissionId != submissionId || string.IsNullOrWhiteSpace(resource.Sha256))
                {
                    return new CapabilityValidationFailure(
                        "Um resource usado na revisao expirou ou nao pode ser validado. Gere um novo draft antes de lancar.",
                        "submission_integrity_resource_unavailable");
                }
                hashes.Add(resource.Sha256);
            }

            var current = await submissionContentHashResolver.ResolveAsync(
                userExternalId,
                payloadItem.AssignmentId,
                payloadItem.StudentId,
                submissionId,
                hashes,
                cancellationToken);
            return string.Equals(current.Hash, item.SubmissionContentHash, StringComparison.Ordinal)
                ? null
                : new CapabilityValidationFailure(
                    "A submissao foi alterada desde a revisao. Gere um novo draft e uma nova previa antes de lancar.",
                    "submission_changed_since_review");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new CapabilityValidationFailure(
                "Nao foi possivel revalidar a integridade atual da submissao antes do lancamento.",
                "submission_integrity_validation_failed");
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
            var currentStatus = await GradingMoodleReadRetry.ExecuteAsync(
                retryCancellationToken => submissionStatusGateway.GetSubmissionStatusAsync(
                    userExternalId,
                    payloadItem.AssignmentId,
                    payloadItem.StudentId,
                    retryCancellationToken),
                onRetry: null,
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

    private async Task<(IReadOnlySet<string>? StudentIds, string? Error)> ResolveCourseEnrollmentAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken)
    {
        try
        {
            var studentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentPage = 1;
            while (true)
            {
                var page = await GetCourseParticipantsWithRetryAsync(
                    userExternalId,
                    courseId,
                    currentPage,
                    cancellationToken);
                foreach (var participant in page.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(participant.UserId))
                    {
                        studentIds.Add(participant.UserId);
                    }

                }

                if (!page.HasMore)
                {
                    return (studentIds, null);
                }

                currentPage++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.GetType().Name);
        }
    }

    private static SubmissionAttemptValidationResult ValidateSubmissionAttempt(
        GradingLaunchPayloadItem payloadItem,
        AssignmentSubmissionAttemptStatus? currentStatus)
    {
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

    private Task<CourseParticipantsPage> GetCourseParticipantsWithRetryAsync(
        string userExternalId,
        string courseId,
        int page,
        CancellationToken cancellationToken)
    {
        return GradingMoodleReadRetry.ExecuteAsync(
            retryCancellationToken => participantsGateway.GetCourseParticipantsAsync(
                userExternalId,
                courseId,
                ParticipantStatusFilter.All,
                page,
                pageSize: 500,
                studentsOnly: false,
                includeEmail: false,
                groupId: null,
                retryCancellationToken),
            onRetry: null,
            cancellationToken);
    }

    private async Task<UnknownWriteReconciliationResult> ReconcileUnknownWriteAsync(
        string userExternalId,
        GradingLaunchPayloadItem payloadItem,
        CancellationToken cancellationToken)
    {
        try
        {
            var existingGrade = await GradingMoodleReadRetry.ExecuteAsync(
                retryCancellationToken => gradeReadGateway.GetExistingGradeAsync(
                    userExternalId,
                    payloadItem.AssignmentId,
                    payloadItem.StudentId,
                    retryCancellationToken),
                onRetry: null,
                cancellationToken);

            if (existingGrade is null ||
                (!existingGrade.HasGrade && string.IsNullOrWhiteSpace(existingGrade.Feedback)))
            {
                return new UnknownWriteReconciliationResult(
                    UnknownWriteReconciliationStatus.NotApplied,
                    "A resposta da escrita foi perdida, mas a leitura posterior confirmou que a nota e o feedback nao foram aplicados. Nenhuma nova escrita foi enviada; gere uma nova previa antes de tentar novamente.");
            }

            if ((payloadItem.Grade is null ||
                 (existingGrade.HasGrade && existingGrade.Grade == payloadItem.Grade)) &&
                FeedbackMatches(existingGrade.Feedback, payloadItem.FeedbackText))
            {
                return new UnknownWriteReconciliationResult(
                    UnknownWriteReconciliationStatus.Applied,
                    payloadItem.Grade is null
                        ? "A resposta da escrita foi perdida, mas a leitura posterior confirmou o feedback no Moodle."
                        : "A resposta da escrita foi perdida, mas a leitura posterior confirmou a nota e o feedback no Moodle.");
            }

            return new UnknownWriteReconciliationResult(
                UnknownWriteReconciliationStatus.Inconclusive,
                "A resposta da escrita foi perdida e a leitura posterior encontrou dados diferentes do lancamento aprovado. Reconcilie manualmente antes de qualquer nova tentativa.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UnknownWriteReconciliationResult(
                UnknownWriteReconciliationStatus.Inconclusive,
                $"A resposta da escrita foi perdida e nao foi possivel reconciliar o resultado no Moodle: {ex.Message}");
        }
    }

    private static bool FeedbackMatches(string? persistedFeedback, string expectedFeedback)
    {
        return string.Equals(
            NormalizeFeedback(persistedFeedback),
            NormalizeFeedback(expectedFeedback),
            StringComparison.Ordinal);
    }

    private static string NormalizeFeedback(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty)
            .Replace('\u00A0', ' ');
        var withoutMarkup = HtmlTagRegex.Replace(decoded, " ");
        return string.Join(
            " ",
            withoutMarkup.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
        // Capability/preflight failures affect the whole publication. Hydrate
        // the item ledger once instead of issuing one SELECT per item in a
        // 10k-item action; audit entries remain per item for traceability.
        var itemsById = await repository.GetItemsAsync(
            payloadItems.Select(payloadItem => payloadItem.GradingItemId).Distinct().ToArray(),
            cancellationToken);
        foreach (var payloadItem in payloadItems)
        {
            if (!itemsById.TryGetValue(payloadItem.GradingItemId, out var item))
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

    private enum UnknownWriteReconciliationStatus
    {
        Applied,
        NotApplied,
        Inconclusive
    }

    private sealed record UnknownWriteReconciliationResult(
        UnknownWriteReconciliationStatus Status,
        string Message);

    private sealed record SubmissionAttemptValidationResult(
        AssignmentSubmissionAttemptStatus? CurrentStatus,
        CapabilityValidationFailure? Failure);

    private sealed record CapabilityValidationFailure(string Message, string ErrorCode);
}

/// <summary>
/// Reads submission attempt/status data once per assignment (the concrete
/// gateway uses mod_assign_get_submissions, capped at Moodle's request limit)
/// instead of issuing one status call per student in a 10k-item run.
/// </summary>
internal sealed record GradingBulkSubmissionStatusReadResult(
    IReadOnlyDictionary<(string AssignmentId, string StudentId), AssignmentSubmissionAttemptStatus> Statuses,
    IReadOnlySet<string> FailedAssignments,
    bool Attempted)
{
    public const int PerItemFallbackLimit = 8;

    public bool TryGet(string assignmentId, string studentId, out AssignmentSubmissionAttemptStatus? status)
    {
        if (Statuses.TryGetValue((assignmentId, studentId), out var found))
        {
            status = found;
            return true;
        }

        status = null;
        return false;
    }
}

internal static class GradingBulkSubmissionStatusReader
{
    public const int PerItemFallbackLimit = GradingBulkSubmissionStatusReadResult.PerItemFallbackLimit;

    public static async Task<GradingBulkSubmissionStatusReadResult> ReadAsync(
        IMoodleAssignmentSubmissionsGateway? submissionsGateway,
        string userExternalId,
        IReadOnlyCollection<string> assignmentIds,
        CancellationToken cancellationToken)
    {
        if (submissionsGateway is null || assignmentIds.Count == 0)
        {
            return new(
                new Dictionary<(string AssignmentId, string StudentId), AssignmentSubmissionAttemptStatus>(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                false);
        }

        var statuses = new Dictionary<(string AssignmentId, string StudentId), AssignmentSubmissionAttemptStatus>();
        var failedAssignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var batches = await GradingMoodleReadRetry.ExecuteAsync(
                retryCancellationToken => submissionsGateway.GetAssignmentSubmissionsBatchAsync(
                    userExternalId,
                    assignmentIds,
                    status: null,
                    since: null,
                    before: null,
                    retryCancellationToken),
                onRetry: null,
                cancellationToken);
            foreach (var batch in batches)
            {
                if (!string.IsNullOrWhiteSpace(batch.ErrorMessage))
                {
                    failedAssignments.Add(batch.AssignmentId);
                    continue;
                }

                foreach (var submission in batch.Submissions)
                {
                    if (string.IsNullOrWhiteSpace(submission.UserId))
                    {
                        continue;
                    }

                    var key = (batch.AssignmentId, submission.UserId);
                    var candidate = new AssignmentSubmissionAttemptStatus(
                        batch.AssignmentId,
                        submission.UserId,
                        submission.AttemptNumber,
                        submission.Status,
                        HasFeedback: !string.IsNullOrWhiteSpace(submission.CurrentFeedback));
                    // Some Moodle variants return every attempt in the bulk
                    // response. Retain the newest attempt so the preflight
                    // compares the draft with the same submission selected by
                    // the item builder.
                    if (!statuses.TryGetValue(key, out var previous) ||
                        (candidate.AttemptNumber ?? -1) >= (previous.AttemptNumber ?? -1))
                    {
                        statuses[key] = candidate;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            foreach (var assignmentId in assignmentIds)
            {
                failedAssignments.Add(assignmentId);
            }
        }

        return new(statuses, failedAssignments, true);
    }
}
