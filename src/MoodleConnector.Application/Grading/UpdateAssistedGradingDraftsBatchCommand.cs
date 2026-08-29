using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

public sealed record BatchDraftUpdateResultDto(int SuccessCount, int FailureCount, IReadOnlyList<Guid> SavedIds, IReadOnlyList<BatchDraftUpdateFailureDto> Failures);
public sealed record BatchDraftUpdateFailureDto(Guid GradingItemId, string Message);
public sealed record UpdateAssistedGradingDraftItemInput(
    Guid GradingItemId,
    decimal? FinalGrade,
    string FinalFeedback,
    string TeacherDecision,
    string? ReviewNotes = null,
    string ExpectedReviewStatus = "NotReviewed",
    string? ExpectedDraftVersionHash = null);

public sealed record UpdateAssistedGradingDraftsBatchCommand(
    Guid BatchJobId,
    IReadOnlyList<UpdateAssistedGradingDraftItemInput> Items) : IRequest<BatchDraftUpdateResultDto>;

public sealed class UpdateAssistedGradingDraftsBatchCommandHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser,
    IMoodleUserResolver moodleUserResolver,
    IMoodleAuditLogRepository auditLogs,
    IGradingOperationTelemetry? telemetry = null)
    : IRequestHandler<UpdateAssistedGradingDraftsBatchCommand, BatchDraftUpdateResultDto>
{
    public async Task<BatchDraftUpdateResultDto> Handle(
        UpdateAssistedGradingDraftsBatchCommand request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (request.BatchJobId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(request.BatchJobId));
        }

        if (request.Items.Count == 0)
        {
            return new BatchDraftUpdateResultDto(0, 0, [], []);
        }

        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");

        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);

        var itemIds = request.Items.Select(i => i.GradingItemId).Distinct().ToArray();
        var itemsDict = await repository.GetItemsAsync(itemIds, cancellationToken);
        var artifactsDict = await repository.ListArtifactsByItemsAsync(itemIds, cancellationToken);
        var snapshotsDict = await repository.ListLatestContextSnapshotsByItemsAsync(itemIds, cancellationToken);

        var savedIds = new List<Guid>();
        var failures = new List<BatchDraftUpdateFailureDto>();

        foreach (var input in request.Items)
        {
            try
            {
                if (!itemsDict.TryGetValue(input.GradingItemId, out var item))
                {
                    throw new InvalidOperationException($"Item {input.GradingItemId} nao encontrado.");
                }

                if (item.BatchId != request.BatchJobId)
                {
                     throw new InvalidOperationException($"Item {input.GradingItemId} nao pertence a este lote.");
                }

                var currentDraftVersionHash = GradingDraftVersionHash.Compute(item);
                if (!string.IsNullOrWhiteSpace(input.ExpectedDraftVersionHash) &&
                    !string.Equals(currentDraftVersionHash, input.ExpectedDraftVersionHash, StringComparison.Ordinal))
                {
                    if (!MatchesExistingReview(item, input))
                    {
                        throw new InvalidOperationException("O rascunho foi alterado desde a ultima leitura. Consulte o item novamente antes de sobrescrever.");
                    }
                }
                else if (!string.Equals(item.ReviewStatus.ToString(), input.ExpectedReviewStatus, StringComparison.OrdinalIgnoreCase))
                {
                    if (!MatchesExistingReview(item, input))
                    {
                        throw new InvalidOperationException("O rascunho foi alterado desde a ultima leitura. Consulte o item novamente antes de sobrescrever.");
                    }
                }

                if (string.IsNullOrWhiteSpace(input.FinalFeedback))
                {
                    throw new ArgumentException("O feedback final revisado e obrigatorio.", nameof(input.FinalFeedback));
                }

                decimal? maxGrade = null;
                if (input.FinalGrade is not null)
                {
                    if (snapshotsDict.TryGetValue(item.Id, out var snapshotDoc) && !string.IsNullOrWhiteSpace(snapshotDoc.PayloadJson))
                    {
                        using var doc = JsonDocument.Parse(snapshotDoc.PayloadJson);
                        if (TryGetProperty(doc.RootElement, "GradingScale", out var scaleElement) && scaleElement.ValueKind == JsonValueKind.Object)
                        {
                            if (TryGetProperty(scaleElement, "MaximumGrade", out var maxGradeElement) && maxGradeElement.ValueKind == JsonValueKind.Number)
                            {
                                maxGrade = maxGradeElement.GetDecimal();
                            }
                            else
                            {
                                throw new InvalidOperationException("A escala maxima da tarefa nao pode ser confirmada no snapshot; nota numerica bloqueada.");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Esta atividade nao possui avaliacao numerica no Moodle. Envie somente feedback e deixe finalGrade vazio.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Snapshot da atividade nao encontrado para validar a nota maxima.");
                    }
                }

                item.ApplyTeacherReview(
                    input.FinalGrade,
                    input.FinalFeedback,
                    currentUser.Subject,
                    moodleUserId,
                    input.TeacherDecision,
                    input.ReviewNotes,
                    maxGrade);

                var artifacts = artifactsDict.GetValueOrDefault(item.Id, Array.Empty<GradingArtifact>());
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
                    RequestSanitizedJson = MoodleConnector.Application.Auditing.AuditPayloadSanitizer.SerializeSanitized(new
                    {
                        gradingItemId = item.Id,
                        batchJobId = item.BatchId,
                        input.FinalGrade,
                        input.TeacherDecision,
                        input.ReviewNotes,
                        input.ExpectedReviewStatus,
                        input.ExpectedDraftVersionHash,
                        draftVersionHash,
                        fileHashes
                    }),
                    ResponseSummaryJson = MoodleConnector.Application.Auditing.AuditPayloadSanitizer.SerializeSanitized(new
                    {
                        reviewStatus = item.ReviewStatus.ToString(),
                        commitStatus = item.CommitStatus.ToString(),
                        reviewedAt = item.ReviewedAt,
                        finalGrade = item.FinalGrade
                    }),
                    Status = "draft_reviewed"
                }, cancellationToken);

                savedIds.Add(item.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                failures.Add(new BatchDraftUpdateFailureDto(input.GradingItemId, ex.Message));
            }
        }

        if (savedIds.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);
        }

        var result = new BatchDraftUpdateResultDto(savedIds.Count, failures.Count, savedIds, failures);
        telemetry?.RecordPhase(
            "grading",
            "review_save",
            failures.Count == 0 ? "success" : "partial_failure",
            stopwatch.Elapsed.TotalMilliseconds,
            queryCount: 4,
            itemCount: savedIds.Count);
        return result;
    }

    private static bool MatchesExistingReview(
        AssistedGradingItem item,
        UpdateAssistedGradingDraftItemInput request)
    {
        return item.FinalGrade == request.FinalGrade &&
            string.Equals(item.FinalFeedback, request.FinalFeedback?.Trim(), StringComparison.Ordinal) &&
            string.Equals(item.TeacherDecision, Normalize(request.TeacherDecision), StringComparison.Ordinal) &&
            string.Equals(item.ReviewNotes, Normalize(request.ReviewNotes), StringComparison.Ordinal);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
