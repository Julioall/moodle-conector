using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Registry;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools.Portal;

namespace MoodleConnector.Presentation.Tools;

/// <summary>
/// Shared adapter used by MCP tools to read the durable snapshot without making
/// cache ownership or refresh policy part of each tool's business logic.
/// </summary>
public sealed class MoodleSnapshotToolContext(
    PortalMcpIdentityResolver identityResolver,
    IConnectionRegistry connectionRegistry,
    IMoodleSnapshotStore snapshotStore,
    IMoodleSnapshotSyncQueue snapshotSyncQueue)
{
    public async Task<MoodleSnapshotToolScope?> TryResolveAsync(
        string? moodleAlias,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var identity = await identityResolver.ResolveAsync(cancellationToken);
            var connection = await connectionRegistry.ResolveConnectionAsync(moodleAlias, cancellationToken);
            if (connection is null)
            {
                return null;
            }

            return new MoodleSnapshotToolScope(
                identity,
                connection.Alias,
                identity.ConnectorClientId ?? identity.Id.ToString("N"));
        }
        catch (InvalidOperationException)
        {
            // Legacy service clients may not have a local portal account. In
            // that case the tool falls back to its existing live path.
            return null;
        }
    }

    public Task<MoodleSnapshotEnvelope<T>?> GetAsync<T>(
        MoodleSnapshotToolScope scope,
        string dataset,
        string courseId = "",
        CancellationToken cancellationToken = default) =>
        snapshotStore.GetAsync<T>(scope.Identity.Id, scope.ConnectionAlias, dataset, courseId, cancellationToken);

    public Task<MoodleSnapshotEnvelope<IReadOnlyList<CourseSummary>>?> GetCoursesAsync(
        MoodleSnapshotToolScope scope,
        CancellationToken cancellationToken = default) =>
        snapshotStore.GetCoursesAsync(scope.Identity.Id, scope.ConnectionAlias, cancellationToken);

    public Task<MoodleSnapshotEnvelope<CourseContentsSummary>?> GetActivitiesAsync(
        MoodleSnapshotToolScope scope,
        string courseId,
        CancellationToken cancellationToken = default) =>
        snapshotStore.GetActivitiesAsync(scope.Identity.Id, scope.ConnectionAlias, courseId, cancellationToken);

    public Task<MoodleSnapshotEnvelope<CourseParticipantsPage>?> GetStudentsAsync(
        MoodleSnapshotToolScope scope,
        string courseId,
        CancellationToken cancellationToken = default) =>
        snapshotStore.GetStudentsAsync(scope.Identity.Id, scope.ConnectionAlias, courseId, cancellationToken);

    public Task<MoodleSnapshotEnvelope<IReadOnlyList<CourseGroupSummary>>?> GetGroupsAsync(
        MoodleSnapshotToolScope scope,
        string courseId,
        CancellationToken cancellationToken = default) =>
        snapshotStore.GetGroupsAsync(scope.Identity.Id, scope.ConnectionAlias, courseId, cancellationToken);

    public Task<bool> QueueAsync(
        MoodleSnapshotToolScope scope,
        string userExternalId,
        string dataset,
        string? courseId,
        int priority,
        bool force = false,
        CancellationToken cancellationToken = default) =>
        snapshotSyncQueue.EnqueueAsync(
            new MoodleSnapshotSyncRequest(
                scope.Identity.Id,
                scope.ClientId,
                scope.ConnectionAlias,
                userExternalId,
                force,
                dataset,
                courseId,
                priority),
            cancellationToken);
}

public sealed record MoodleSnapshotToolScope(
    PortalMcpIdentity Identity,
    string ConnectionAlias,
    string ClientId);
