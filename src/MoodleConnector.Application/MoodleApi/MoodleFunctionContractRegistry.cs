using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MoodleConnector.Application.MoodleApi;

/// <summary>
/// In-memory contract registry. It deliberately has no connection or
/// credential dependency: a contract describes a function, it does not grant
/// the current token access to it.
/// </summary>
public sealed class MoodleFunctionContractRegistry : IMoodleFunctionContractRegistry
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new();
    private readonly IReadOnlyList<MoodleFunctionContract> _contracts;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<MoodleFunctionContract>> _byFunction;

    public MoodleFunctionContractRegistry(IEnumerable<MoodleFunctionContract> contracts)
    {
        _contracts = contracts
            .Where(contract => !string.IsNullOrWhiteSpace(contract.FunctionName))
            .Select(contract => contract with { FunctionName = contract.FunctionName.Trim() })
            .ToArray();
        _byFunction = _contracts
            .GroupBy(contract => contract.FunctionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MoodleFunctionContract>)group.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public MoodleFunctionContractResolution Resolve(
        string functionName,
        string? moodleRelease = null,
        string? externalFunctionVersion = null)
    {
        var normalizedName = functionName?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0 || !_byFunction.TryGetValue(normalizedName, out var candidates))
        {
            return Missing(normalizedName, "contract_not_found");
        }

        var compatible = candidates
            .Where(contract => IsMoodleReleaseCompatible(contract.MoodleVersion, moodleRelease) &&
                               IsVersionCompatible(contract.ExternalFunctionVersion, externalFunctionVersion))
            .ToArray();
        if (compatible.Length == 0)
        {
            return new MoodleFunctionContractResolution(
                normalizedName,
                MoodleContractResolutionStatus.Stale,
                null,
                ["schema_version_mismatch"]);
        }

        var verified = compatible
            .Where(contract => contract.Status == MoodleContractStatus.Verified)
            .ToArray();
        if (verified.Length == 0)
        {
            var status = compatible.Any(contract => contract.Status == MoodleContractStatus.Conflicting)
                ? MoodleContractResolutionStatus.Conflicting
                : compatible.Any(contract => contract.Status == MoodleContractStatus.Invalid)
                    ? MoodleContractResolutionStatus.Invalid
                    : compatible.Any(contract => contract.Status == MoodleContractStatus.Stale)
                        ? MoodleContractResolutionStatus.Stale
                        : MoodleContractResolutionStatus.Missing;
            return new MoodleFunctionContractResolution(
                normalizedName,
                status,
                null,
                compatible.Select(contract => contract.Status.ToString().ToLowerInvariant()).Distinct().ToArray());
        }

        var maxSpecificity = verified.Max(contract => Specificity(contract, moodleRelease));
        var mostSpecific = verified
            .Where(contract => Specificity(contract, moodleRelease) == maxSpecificity)
            .ToArray();
        var hashes = mostSpecific.Select(contract => contract.ContractHash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hashes.Length != 1)
        {
            return new MoodleFunctionContractResolution(
                normalizedName,
                MoodleContractResolutionStatus.Conflicting,
                null,
                ["multiple_verified_contract_hashes"]);
        }

        var selected = mostSpecific[0];
        var integrityErrors = ValidateIntegrity(selected);
        return integrityErrors.Count == 0
            ? new MoodleFunctionContractResolution(normalizedName, MoodleContractResolutionStatus.Verified, selected, [])
            : new MoodleFunctionContractResolution(normalizedName, MoodleContractResolutionStatus.Invalid, null, integrityErrors);
    }

    public IReadOnlyList<MoodleFunctionContractResolution> Evaluate(MoodleFunctionProfile profile) =>
        profile.Functions
            .Where(function => function.IsAvailable)
            .Select(function => Resolve(function.Name, profile.Release, function.ExternalFunctionVersion))
            .ToArray();

    public static string ComputeContractHash(MoodleFunctionContract contract)
    {
        var canonicalParts = new List<string>
        {
            contract.FunctionName.Trim().ToLowerInvariant(),
            contract.Effect.ToString().ToLowerInvariant(),
            CanonicalizeJson(contract.InputSchema),
            CanonicalizeJson(contract.OutputSchema),
            contract.MoodleVersion ?? string.Empty,
            contract.Component ?? string.Empty,
            contract.PluginVersion ?? string.Empty,
            contract.Source ?? string.Empty,
            JsonSerializer.Serialize(contract.RequiredScopes ?? []),
            contract.PlatformPermission ?? string.Empty,
            JsonSerializer.Serialize(contract.Pagination),
            JsonSerializer.Serialize(contract.FileHandling)
        };

        // Capability metadata is evidence from the Moodle installation, not
        // an authorization grant. Bind it to new hashes when present while
        // preserving hashes from manifests created before this field existed.
        if (contract.MoodleCapabilities is not null)
        {
            canonicalParts.Insert(9, JsonSerializer.Serialize(contract.MoodleCapabilities));
        }

        // Optional metadata is appended only when declared. This preserves
        // hashes generated by schemaVersion=1 manifests created before these
        // fields existed, while still binding new manifests to the exact
        // External Function version and administrative classification.
        if (!string.IsNullOrWhiteSpace(contract.ExternalFunctionVersion))
        {
            canonicalParts.Insert(5, contract.ExternalFunctionVersion.Trim());
        }
        if (contract.AdministrativeOnly)
        {
            canonicalParts.Add("administrativeOnly:true");
        }

        var canonical = string.Join("\n", canonicalParts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string CanonicalizeJson(JsonElement value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    private static IReadOnlyList<string> ValidateIntegrity(MoodleFunctionContract contract)
    {
        var errors = new List<string>();
        if (contract.Effect == MoodleEffect.Unknown)
        {
            errors.Add("contract_effect_unknown");
        }
        if (contract.InputSchema.ValueKind != JsonValueKind.Object)
        {
            errors.Add("input_schema_invalid");
        }
        if (contract.OutputSchema.ValueKind != JsonValueKind.Object)
        {
            errors.Add("output_schema_invalid");
        }
        if (string.IsNullOrWhiteSpace(contract.ContractHash) ||
            !string.Equals(contract.ContractHash, ComputeContractHash(contract), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("contract_hash_invalid");
        }
        return errors;
    }

    private static bool IsVersionCompatible(string? contractVersion, string? moodleRelease)
    {
        // A contract without a release is a deliberately generic contract. A
        // versioned contract is never silently applied to a different version.
        return string.IsNullOrWhiteSpace(contractVersion) ||
               string.Equals(contractVersion.Trim(), moodleRelease?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMoodleReleaseCompatible(string? contractRelease, string? discoveredRelease)
    {
        // Moodle's CFG->release and site_info may differ only by the build
        // suffix, for example "5.1.2" versus "5.1.2 (Build: 20260209)".
        // Keep the patch release strict while ignoring that non-semantic
        // suffix. If a value is not a semantic release, retain exact matching.
        if (string.IsNullOrWhiteSpace(contractRelease))
        {
            return true;
        }

        var normalizedContract = NormalizeMoodleRelease(contractRelease);
        var normalizedDiscovered = NormalizeMoodleRelease(discoveredRelease);
        return string.Equals(normalizedContract, normalizedDiscovered, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMoodleRelease(string? release)
    {
        var value = release?.Trim() ?? string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(value, @"(?<!\d)(\d+\.\d+\.\d+)(?!\d)");
        return match.Success ? match.Groups[1].Value : value;
    }

    private static int Specificity(MoodleFunctionContract contract, string? moodleRelease) =>
        (IsMoodleReleaseCompatible(contract.MoodleVersion, moodleRelease) &&
         !string.IsNullOrWhiteSpace(contract.MoodleVersion) ? 2 : 0) +
        (!string.IsNullOrWhiteSpace(contract.ExternalFunctionVersion) ? 2 : 0) +
        (!string.IsNullOrWhiteSpace(contract.Component) ? 1 : 0) +
        (!string.IsNullOrWhiteSpace(contract.PluginVersion) ? 1 : 0);

    private static MoodleFunctionContractResolution Missing(string functionName, string reason) =>
        new(functionName, MoodleContractResolutionStatus.Missing, null, [reason]);
}
