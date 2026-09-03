using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleResourceRepository(ConnectorDbContext dbContext) : IMoodleResourceRepository
{
    public Task RegisterAsync(MoodleResource resource, CancellationToken cancellationToken) =>
        dbContext.MoodleResources.AddAsync(resource, cancellationToken).AsTask();

    public Task<MoodleResource?> FindAsync(string resourceId, CancellationToken cancellationToken) =>
        dbContext.MoodleResources.SingleOrDefaultAsync(resource => resource.ResourceId == resourceId, cancellationToken);

    public async Task<IReadOnlyList<MoodleResource>> ListBySubmissionAsync(
        string clientId,
        string connectionId,
        long submissionId,
        CancellationToken cancellationToken) =>
        await dbContext.MoodleResources
            .Where(resource =>
                resource.ClientId == clientId &&
                resource.ConnectionId == connectionId &&
                resource.SubmissionId == submissionId &&
                resource.ParentResourceId == null)
            .OrderBy(resource => resource.ResourceId)
            .ToArrayAsync(cancellationToken);

    public Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        dbContext.MoodleResources.Where(resource => resource.ExpiresAt <= now || resource.RevokedAt != null)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<bool> ExistsAndNotExpiredAsync(string resourceId, DateTimeOffset now, CancellationToken cancellationToken) =>
        dbContext.MoodleResources.AnyAsync(
            resource => resource.ResourceId == resourceId &&
                        resource.RevokedAt == null &&
                        resource.ExpiresAt > now,
            cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
