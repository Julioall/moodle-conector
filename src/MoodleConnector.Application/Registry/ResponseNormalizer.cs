using System.Text.Json.Nodes;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public sealed class ResponseNormalizer : IResponseNormalizer
{
    public JsonNode? Normalize(string profileName, JsonNode? rawResponse, NormalizationContext? context = null)
    {
        context ??= new NormalizationContext();
        
        if (rawResponse is null) return null;

        if (context.Mode == NormalizationMode.Agent && rawResponse is JsonArray array)
        {
            if (array.Count > context.MaxItems)
            {
                var safeArray = new JsonArray();
                foreach (var item in array.Take(context.MaxItems))
                {
                    var clone = JsonNode.Parse(item?.ToJsonString() ?? "null");
                    safeArray.Add(clone);
                }
                var wrapper = new JsonObject
                {
                    ["items"] = safeArray,
                    ["returned"] = context.MaxItems,
                    ["total"] = array.Count,
                    ["hasMore"] = true,
                    ["truncated"] = true
                };
                return wrapper;
            }
        }

        return rawResponse;
    }
}
