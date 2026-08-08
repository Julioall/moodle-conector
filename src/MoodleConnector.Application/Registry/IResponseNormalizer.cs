using System.Text.Json.Nodes;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public interface IResponseNormalizer
{
    JsonNode? Normalize(string profileName, JsonNode? rawResponse, NormalizationContext? context = null);
}
