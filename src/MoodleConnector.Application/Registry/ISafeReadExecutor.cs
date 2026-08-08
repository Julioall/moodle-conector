using System.Text.Json.Nodes;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public interface ISafeReadExecutor
{
    Task<JsonNode?> ExecuteAsync(
        string operationName, 
        Dictionary<string, object?> parameters, 
        string? moodleAlias = null, 
        NormalizationContext? context = null,
        CancellationToken cancellationToken = default);
}
