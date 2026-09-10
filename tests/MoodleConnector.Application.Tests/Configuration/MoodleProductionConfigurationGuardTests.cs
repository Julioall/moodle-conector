using MoodleConnector.Application.Configuration;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Application.Tests.Configuration;

public sealed class MoodleProductionConfigurationGuardTests
{
    [Fact]
    public void Production_rejects_universal_write_without_strict_contracts()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MoodleProductionConfigurationGuard.Validate(
                isProduction: true,
                new MoodleUniversalApiFeatureOptions { UniversalMoodleWriteEnabled = true },
                new MoodleFunctionContractOptions { RequireVerifiedContracts = false }));

        Assert.Contains("RequireVerifiedContracts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_accepts_universal_write_with_strict_contracts()
    {
        var exception = Record.Exception(() =>
            MoodleProductionConfigurationGuard.Validate(
                isProduction: true,
                new MoodleUniversalApiFeatureOptions { UniversalMoodleWriteEnabled = true },
                new MoodleFunctionContractOptions
                {
                    ContractManifestPath = "/app/contracts/production.json",
                    RequireVerifiedContracts = true
                }));

        Assert.Null(exception);
    }

    [Fact]
    public void Production_rejects_missing_contract_manifest_path()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MoodleProductionConfigurationGuard.Validate(
                isProduction: true,
                new MoodleUniversalApiFeatureOptions(),
                new MoodleFunctionContractOptions { RequireVerifiedContracts = true }));

        Assert.Contains("ContractManifestPath", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/tmp/production.json")]
    [InlineData("/app/contracts/nested/production.json")]
    [InlineData("/app/contracts/../production.json")]
    public void Production_rejects_manifest_path_outside_contract_mount(string manifestPath)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MoodleProductionConfigurationGuard.Validate(
                isProduction: true,
                new MoodleUniversalApiFeatureOptions(),
                new MoodleFunctionContractOptions
                {
                    ContractManifestPath = manifestPath,
                    RequireVerifiedContracts = true
                }));

        Assert.Contains("/app/contracts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_rejects_transitional_contract_mode_even_when_universal_writes_are_off()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MoodleProductionConfigurationGuard.Validate(
                isProduction: true,
                new MoodleUniversalApiFeatureOptions(),
                new MoodleFunctionContractOptions { RequireVerifiedContracts = false }));

        Assert.Contains("Production", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RequireVerifiedContracts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_rejects_universal_upload_without_strict_contracts()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MoodleProductionConfigurationGuard.Validate(
                isProduction: true,
                new MoodleUniversalApiFeatureOptions { UniversalMoodleFileUploadEnabled = true },
                new MoodleFunctionContractOptions { RequireVerifiedContracts = false }));

        Assert.Contains("RequireVerifiedContracts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_production_preserves_transitional_rollout()
    {
        var exception = Record.Exception(() =>
            MoodleProductionConfigurationGuard.Validate(
                isProduction: false,
                new MoodleUniversalApiFeatureOptions { UniversalMoodleWriteEnabled = true },
                new MoodleFunctionContractOptions { RequireVerifiedContracts = false }));

        Assert.Null(exception);
    }
}
