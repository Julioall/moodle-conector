using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Presentation.Configuration;

internal static class MoodleProductionConfigurationGuard
{
    public static void Validate(
        bool isProduction,
        MoodleUniversalApiFeatureOptions universalFeatures,
        MoodleFunctionContractOptions contractOptions)
    {
        ArgumentNullException.ThrowIfNull(universalFeatures);
        ArgumentNullException.ThrowIfNull(contractOptions);

        if (isProduction && !contractOptions.RequireVerifiedContracts)
        {
            throw new InvalidOperationException(
                "Production exige MoodleApi:RequireVerifiedContracts=true para a superficie universal do Moodle.");
        }

        if (!isProduction)
        {
            return;
        }

        var manifestPath = contractOptions.ContractManifestPath?.Trim();
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new InvalidOperationException(
                "Production exige MoodleApi:ContractManifestPath apontando para um manifesto verificado.");
        }

        var normalizedManifestPath = manifestPath.Replace('\\', '/');
        const string contractsPrefix = "/app/contracts/";
        var relativeManifestPath = normalizedManifestPath.StartsWith(contractsPrefix, StringComparison.Ordinal)
            ? normalizedManifestPath[contractsPrefix.Length..]
            : null;

        if (string.IsNullOrWhiteSpace(relativeManifestPath) ||
            relativeManifestPath.Contains("/", StringComparison.Ordinal) ||
            relativeManifestPath is "." or ".." ||
            relativeManifestPath.Contains("..", StringComparison.Ordinal) ||
            relativeManifestPath.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Production exige MoodleApi:ContractManifestPath como arquivo direto sob /app/contracts.");
        }
    }
}
