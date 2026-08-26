using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace MoodleConnector.Infrastructure;

public static class MoodleConnectionIdentity
{
    public static async Task<string> ResolveAsync(
        ConnectorDbContext db,
        Guid ownerId,
        string clientId,
        string connectionAlias,
        CancellationToken cancellationToken)
    {
        var normalizedAlias = connectionAlias.Trim().ToLowerInvariant();
        var resolvedClientId = string.IsNullOrWhiteSpace(clientId)
            ? await db.UserAccounts
                .AsNoTracking()
                .Where(item => item.Id == ownerId)
                .Select(item => item.ConnectorClientId)
                .SingleOrDefaultAsync(cancellationToken)
            : clientId.Trim();

        if (!string.IsNullOrWhiteSpace(resolvedClientId))
        {
            var connectionId = await db.ConnectorClients
                .AsNoTracking()
                .Where(item => item.ClientId == resolvedClientId &&
                               item.MoodleAlias.ToLower() == normalizedAlias &&
                               item.IsActive)
                .Select(item => item.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(connectionId))
            {
                return connectionId;
            }
        }

        // Legacy service clients may not have a local connector row. This
        // deterministic key is only a compatibility bridge until they are
        // registered; it is never presented as a Moodle alias.
        var source = $"legacy:{ownerId:N}:{resolvedClientId}:{normalizedAlias}";
        return $"legacy-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()}";
    }
}
