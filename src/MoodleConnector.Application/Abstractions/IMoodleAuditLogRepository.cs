using MoodleConnector.Domain;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleAuditLogRepository
{
    Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
