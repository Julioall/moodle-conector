namespace MoodleConnector.Application.Configuration;

public sealed class MoodleFunctionContractOptions
{
    // Bound from MoodleApi so the contract registry remains separate from the
    // connection/token capability registry while using one configuration root.
    public const string SectionName = "MoodleApi";

    public string? ContractManifestPath { get; init; }

    /// <summary>
    /// Transitional rollout switch. When false, contracts enrich and validate
    /// calls that have a verified entry, while legacy discovered functions keep
    /// their existing conservative path. When true, missing/incompatible
    /// contracts fail closed before a remote call.
    /// </summary>
    public bool RequireVerifiedContracts { get; init; }

    public int MaxManifestBytes { get; init; } = 5 * 1024 * 1024;
}
