using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public sealed record MoodleSnapshotSyncRequest(
    Guid OwnerId,
    string ClientId,
    string ConnectionAlias,
    string UserExternalId,
    bool Force = false);

public sealed record MoodleSnapshotEnvelope<T>(
    T Data,
    DateTimeOffset UpdatedAt,
    bool IsStale,
    bool IsFrozen,
    string Tier);

public interface IMoodleSnapshotStore
{
    Task<MoodleSnapshotEnvelope<IReadOnlyList<CourseSummary>>?> GetCoursesAsync(
        Guid ownerId,
        string connectionAlias,
        CancellationToken cancellationToken = default);

    Task<MoodleSnapshotEnvelope<CourseContentsSummary>?> GetActivitiesAsync(
        Guid ownerId,
        string connectionAlias,
        string courseId,
        CancellationToken cancellationToken = default);

    Task<MoodleSnapshotEnvelope<CourseParticipantsPage>?> GetStudentsAsync(
        Guid ownerId,
        string connectionAlias,
        string courseId,
        CancellationToken cancellationToken = default);
}

public interface IMoodleSnapshotSyncQueue
{
    bool Enqueue(MoodleSnapshotSyncRequest request);
}
