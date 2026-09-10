using System.Text.Json;

namespace MoodleConnector.Application.MoodleApi;

/// <summary>
/// Effect declared by a verified external-function contract. Function names
/// are not authoritative because Moodle functions may update completion,
/// logging, or other state while looking like a query.
/// </summary>
public enum MoodleEffect
{
    Unknown = 0,
    Read,
    Write
}

public enum MoodleContractStatus
{
    Missing = 0,
    Verified,
    Stale,
    Conflicting,
    Invalid
}

public sealed record MoodlePaginationContract(
    string Mode,
    string? OffsetParameter = null,
    string? LimitParameter = null,
    string? PageParameter = null,
    string? PageSizeParameter = null,
    string? CursorParameter = null,
    string? HasMoreProperty = null,
    string? TotalProperty = null,
    string? NextCursorProperty = null);

public sealed record MoodleFileHandlingContract(
    bool AcceptsFiles,
    bool ReturnsFiles,
    string? Destination = null,
    IReadOnlyList<string>? AllowedMimeTypes = null);

/// <summary>
/// Versioned, externally sourced contract for one Moodle External Function.
/// The schemas are JSON Schema documents; they are never interpreted as code.
/// </summary>
public sealed record MoodleFunctionContract
{
    public required string FunctionName { get; init; }
    public required MoodleEffect Effect { get; init; }
    public required JsonElement InputSchema { get; init; }
    public required JsonElement OutputSchema { get; init; }
    public string? MoodleVersion { get; init; }
    public string? ExternalFunctionVersion { get; init; }
    public string? Component { get; init; }
    public string? PluginVersion { get; init; }
    public required MoodleContractStatus Status { get; init; }
    public required string ContractHash { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<string>? RequiredScopes { get; init; }
    public string? PlatformPermission { get; init; }
    public bool AdministrativeOnly { get; init; }
    public MoodlePaginationContract? Pagination { get; init; }
    public MoodleFileHandlingContract? FileHandling { get; init; }
    /// <summary>
    /// Capability strings exported from Moodle as evidence. They do not grant
    /// access; the current Moodle token and contextual authorization remain
    /// authoritative.
    /// </summary>
    public IReadOnlyList<string>? MoodleCapabilities { get; init; }
}

public enum MoodleContractResolutionStatus
{
    Missing = 0,
    Verified,
    Stale,
    Conflicting,
    Invalid
}

public sealed record MoodleFunctionContractResolution(
    string FunctionName,
    MoodleContractResolutionStatus Status,
    MoodleFunctionContract? Contract,
    IReadOnlyList<string> Reasons)
{
    public bool IsVerified => Status == MoodleContractResolutionStatus.Verified && Contract is not null;

    public string? ContractHash => Contract?.ContractHash;
}

public interface IMoodleFunctionContractRegistry
{
    MoodleFunctionContractResolution Resolve(
        string functionName,
        string? moodleRelease = null,
        string? externalFunctionVersion = null);

    IReadOnlyList<MoodleFunctionContractResolution> Evaluate(MoodleFunctionProfile profile);
}

public static class MoodleFunctionContractSchemaValidator
{
    public static IReadOnlyList<string> ValidateInput(
        MoodleFunctionContract contract,
        IReadOnlyDictionary<string, object?> parameters) =>
        Validate(contract.InputSchema, Serialize(parameters), "parameters");

    public static IReadOnlyList<string> ValidateOutput(
        MoodleFunctionContract contract,
        JsonElement payload) =>
        Validate(contract.OutputSchema, payload, "response");

    private static IReadOnlyList<string> Validate(
        JsonElement schema,
        JsonElement value,
        string path)
    {
        var errors = new List<string>();
        ValidateNode(schema, value, path, errors);
        return errors;
    }

    private static void ValidateNode(JsonElement schema, JsonElement value, string path, ICollection<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: schema must be a JSON object.");
            return;
        }

        if (schema.TryGetProperty("enum", out var enumValues) && enumValues.ValueKind == JsonValueKind.Array &&
            !enumValues.EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value)))
        {
            errors.Add($"{path}: value is not one of the declared enum values.");
        }

        if (schema.TryGetProperty("type", out var typeElement))
        {
            var matches = typeElement.ValueKind == JsonValueKind.Array
                ? typeElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Any(type => MatchesType(type, value))
                : typeElement.ValueKind == JsonValueKind.String && MatchesType(typeElement.GetString(), value);
            if (!matches)
            {
                errors.Add($"{path}: expected JSON type '{typeElement.GetRawText()}'.");
                return;
            }
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            ValidateRequired(schema, value, path, errors);
            ValidateProperties(schema, value, path, errors);
        }

        if (value.ValueKind == JsonValueKind.Array &&
            schema.TryGetProperty("items", out var itemSchema))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateNode(itemSchema, item, $"{path}[{index}]", errors);
                index++;
            }
        }
    }

    private static void ValidateRequired(JsonElement schema, JsonElement value, string path, ICollection<string> errors)
    {
        if (!schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var property in required.EnumerateArray())
        {
            if (property.ValueKind == JsonValueKind.String &&
                !value.TryGetProperty(property.GetString()!, out _))
            {
                errors.Add($"{path}: required property '{property.GetString()}' is missing.");
            }
        }
    }

    private static void ValidateProperties(JsonElement schema, JsonElement value, string path, ICollection<string> errors)
    {
        var hasProperties = schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object;
        if (hasProperties)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    ValidateNode(propertySchema, property.Value, $"{path}.{property.Name}", errors);
                }
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additional) &&
            additional.ValueKind == JsonValueKind.False)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!hasProperties || !properties.TryGetProperty(property.Name, out _))
                {
                    errors.Add($"{path}: property '{property.Name}' is not allowed.");
                }
            }
        }
    }

    private static bool MatchesType(string? type, JsonElement value) =>
        type?.ToLowerInvariant() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => false
        };

    private static JsonElement Serialize(IReadOnlyDictionary<string, object?> parameters)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(parameters));
        return document.RootElement.Clone();
    }
}
