using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Resolves the compatibility <c>batchJobId</c> contract to either one child
/// batch or all batches belonging to a durable <c>gradingRunId</c>.  Every
/// child is authorized independently, preventing a run handle from becoming a
/// cross-user data leak.
/// </summary>
internal static class GradingBatchScopeResolver
{
    public static async Task<GradingBatchScope> ResolveAsync(
        IGradingReviewRepository repository,
        ICurrentUserContext currentUser,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("O identificador do lote ou da execucao e obrigatorio.", nameof(id));
        }

        var directBatch = await repository.GetBatchAsync(id, cancellationToken);
        if (directBatch is not null)
        {
            GradingAccessControl.EnsureCanAccessBatch(directBatch, currentUser);
            // A legacy caller may still address a child by batchJobId. Keep
            // the child scope for compatibility, but also resolve its parent
            // run so CSV and Moodle publication share the same destination
            // mutex.
            var linkedRun = directBatch.GradingRunId is Guid runId
                ? await repository.GetGradingRunAsync(runId, cancellationToken)
                : null;
            return new GradingBatchScope(id, null, [directBatch], linkedRun);
        }

        var run = await repository.GetGradingRunAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Lote ou execucao de correcao nao encontrado.");
        if (!string.Equals(run.CreatedBySubject, currentUser.Subject, StringComparison.Ordinal) &&
            !currentUser.HasScope("grading.admin") &&
            !currentUser.HasPlatformPermission("tool.assignments.grade"))
        {
            throw new UnauthorizedAccessException("Usuario atual nao esta autorizado a acessar esta execucao de correcao.");
        }

        var batches = await repository.ListBatchesByGradingRunAsync(run.Id, cancellationToken);
        foreach (var batch in batches)
        {
            GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);
        }

        return new GradingBatchScope(id, run, batches, run);
    }

    public static string? ResolveConnectionKey(AssistedGradingBatch batch)
    {
        if (!string.IsNullOrWhiteSpace(batch.MoodleConnectionId))
        {
            return batch.MoodleConnectionId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(batch.ConnectorClientId) &&
            !string.IsNullOrWhiteSpace(batch.ConnectionAlias))
        {
            return $"{batch.ConnectorClientId.Trim()}:{batch.ConnectionAlias.Trim()}";
        }

        return null;
    }
}

internal sealed record GradingBatchScope(
    Guid RequestedId,
    GradingRun? Run,
    IReadOnlyList<AssistedGradingBatch> Batches,
    GradingRun? DestinationRun = null)
{
    public bool IsRun => Run is not null;

    public AssistedGradingBatch? FirstBatch => Batches.Count == 0 ? null : Batches[0];

    public long? CourseId => Batches.Count == 0
        ? null
        : Batches.Select(batch => batch.CourseId).Distinct().Count() == 1
            ? Batches[0].CourseId
            : null;

    public IReadOnlyList<long> AssignmentIds => Batches
        .SelectMany(batch => batch.AssignmentIds)
        .Distinct()
        .OrderBy(id => id)
        .ToArray();
}
