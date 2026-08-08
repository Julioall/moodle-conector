using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public interface IConnectionRegistry
{
    Task<ConnectionInfo?> ResolveConnectionAsync(string? alias, CancellationToken cancellationToken = default);
}
