using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
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
    [property: JsonPropertyName("items")] IReadOnlyList<AssistedGradingBatchStatusItem> Items);

public sealed record AssistedGradingBatchStatusItem(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reviewStatus")] string ReviewStatus,
    [property: JsonPropertyName("commitStatus")] string CommitStatus);

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
    [property: JsonPropertyName("reviewNotes")] string? ReviewNotes);

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
    IMoodleAuditLogRepository auditLogs)
    : IRequestHandler<CreateAssistedGradingBatchCommand, CreateAssistedGradingBatchResult>
{
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
                        submission.AttemptNumber));
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
        foreach (var seed in selectedItems)
        {
            await repository.AddItemAsync(
                AssistedGradingItem.Create(
                    batch.Id,
                    ParsePositiveLong(seed.CourseId, "courseId"),
                    ParsePositiveLong(seed.AssignmentId, "assignmentId"),
                    ParseNullablePositiveLong(seed.SubmissionId, "submissionId"),
                    ParsePositiveLong(seed.StudentId, "studentId"),
                    seed.AttemptNumber),
                cancellationToken);
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

    private sealed record AssistedGradingItemSeed(
        string CourseId,
        string AssignmentId,
        string? SubmissionId,
        string StudentId,
        int? AttemptNumber);
}

public sealed class GetAssistedGradingItemQueryHandler(
    IGradingReviewRepository repository)
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

        if (request.BatchJobId is Guid batchJobId && batchJobId != Guid.Empty && item.BatchId != batchJobId)
        {
            throw new InvalidOperationException("O item informado nao pertence ao lote solicitado.");
        }

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
            item.ReviewNotes);
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

        if (!string.Equals(item.ReviewStatus.ToString(), request.ExpectedReviewStatus, StringComparison.OrdinalIgnoreCase))
        {
            if (MatchesExistingReview(item, request))
            {
                return ToDetailResult(item);
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
        var draftVersionHash = ComputeDraftVersionHash(item);

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

        return ToDetailResult(item);
    }

    private static string ComputeDraftVersionHash(AssistedGradingItem item)
    {
        var payload = string.Join(
            "|",
            item.Id.ToString("N"),
            item.BatchId.ToString("N"),
            item.FinalGrade?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            item.FinalFeedback ?? string.Empty,
            item.TeacherDecision ?? string.Empty,
            item.ReviewNotes ?? string.Empty,
            item.ReviewedAt?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
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

    private static AssistedGradingItemDetailResult ToDetailResult(AssistedGradingItem item)
    {
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
            item.ReviewNotes);
    }
}

public sealed class GetAssistedGradingBatchStatusQueryHandler(
    IGradingReviewRepository repository)
    : IRequestHandler<GetAssistedGradingBatchStatusQuery, AssistedGradingBatchStatusResult>
{
    public async Task<AssistedGradingBatchStatusResult> Handle(
        GetAssistedGradingBatchStatusQuery request,
        CancellationToken cancellationToken)
    {
        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var items = await repository.ListItemsByBatchAsync(batch.Id, page, pageSize + 1, cancellationToken);
        var totalItems = await repository.CountItemsByBatchAsync(batch.Id, cancellationToken);

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
            items.Take(pageSize).Select(ToStatusItem).ToArray());
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
}
