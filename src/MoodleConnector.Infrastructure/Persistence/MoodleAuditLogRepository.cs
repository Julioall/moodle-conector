using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

public sealed class MoodleAuditLogRepository(ConnectorDbContext dbContext) : IMoodleAuditLogRepository
{
    public async Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken)
    {
        await dbContext.MoodleAuditLogs.AddAsync(log, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
