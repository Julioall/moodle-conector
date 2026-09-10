namespace MoodleConnector.Domain.Registry;

public sealed record CapabilitySnapshot(
    Guid ConnectionId,
    string CredentialFingerprint,
    HashSet<string> AvailableFunctions,
    DateTimeOffset CapturedAt,
    string? MoodleRelease = null,
    IReadOnlyDictionary<string, string?>? FunctionVersions = null
)
{
    public bool IsFunctionAvailable(string functionName)
    {
        return AvailableFunctions.Contains(functionName);
    }

    public string? GetFunctionVersion(string functionName)
    {
        return FunctionVersions is not null && FunctionVersions.TryGetValue(functionName, out var version)
            ? version
            : null;
    }
}
