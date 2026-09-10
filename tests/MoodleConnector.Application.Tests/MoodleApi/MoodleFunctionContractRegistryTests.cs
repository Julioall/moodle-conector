using System.Text.Json;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Application.Tests.MoodleApi;

public sealed class MoodleFunctionContractRegistryTests
{
    [Fact]
    public void Resolve_ReturnsVerifiedContractOnlyWhenHashAndVersionMatch()
    {
        var contract = CreateContract("local_example_get_items", MoodleEffect.Read, "4.5");
        var registry = new MoodleFunctionContractRegistry([contract]);

        var result = registry.Resolve("LOCAL_EXAMPLE_GET_ITEMS", "4.5");

        Assert.True(result.IsVerified);
        Assert.Equal(contract.ContractHash, result.ContractHash);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Resolve_DoesNotTreatKnownContractAsConnectionCapability()
    {
        var contract = CreateContract("local_example_get_items", MoodleEffect.Read);
        var registry = new MoodleFunctionContractRegistry([contract]);
        var profile = new MoodleFunctionProfile(
            "connection-b",
            "secondary",
            "Moodle",
            "4.5",
            10,
            [new MoodleFunctionDescriptor("local_other_get_items", MoodleFunctionRisk.Read, true)],
            DateTimeOffset.UtcNow);

        var result = registry.Evaluate(profile);

        var only = Assert.Single(result);
        Assert.Equal("local_other_get_items", only.FunctionName);
        Assert.Equal(MoodleContractResolutionStatus.Missing, only.Status);
    }

    [Fact]
    public void Resolve_ReturnsVersionMismatchInsteadOfApplyingStaleContract()
    {
        var registry = new MoodleFunctionContractRegistry([CreateContract("local_example_get_items", MoodleEffect.Read, "4.5")]);

        var result = registry.Resolve("local_example_get_items", "4.4");

        Assert.Equal(MoodleContractResolutionStatus.Stale, result.Status);
        Assert.Contains("schema_version_mismatch", result.Reasons);
        Assert.Null(result.Contract);
    }

    [Fact]
    public void Resolve_AllowsMoodleBuildSuffixWhenPatchReleaseMatches()
    {
        var registry = new MoodleFunctionContractRegistry([
            CreateContract("local_example_get_items", MoodleEffect.Read, "5.1.2")
        ]);

        var result = registry.Resolve(
            "local_example_get_items",
            "5.1.2 (Build: 20260209)");

        Assert.True(result.IsVerified);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Resolve_PreservesMissingStatusForKnownUnverifiedContract()
    {
        var contract = CreateContract("local_example_get_items", MoodleEffect.Read) with
        {
            Status = MoodleContractStatus.Missing,
            ContractHash = string.Empty
        };
        var registry = new MoodleFunctionContractRegistry([contract]);

        var result = registry.Resolve(contract.FunctionName);

        Assert.Equal(MoodleContractResolutionStatus.Missing, result.Status);
        Assert.Contains("missing", result.Reasons);
        Assert.Null(result.Contract);
    }

    [Fact]
    public void Resolve_RequiresMatchingExternalFunctionVersionWhenContractDeclaresOne()
    {
        var contract = CreateContract("local_example_get_items", MoodleEffect.Read, "4.5") with
        {
            ExternalFunctionVersion = "2026012100"
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var registry = new MoodleFunctionContractRegistry([contract]);

        var compatible = registry.Resolve(contract.FunctionName, "4.5", "2026012100");
        var stale = registry.Resolve(contract.FunctionName, "4.5", "2026031700");

        Assert.True(compatible.IsVerified);
        Assert.Equal(MoodleContractResolutionStatus.Stale, stale.Status);
        Assert.Contains("schema_version_mismatch", stale.Reasons);
    }

    [Fact]
    public void Resolve_PreservesMoodleCapabilitiesAsEvidenceWithoutGrantingAccess()
    {
        var contract = CreateContract("local_admin_get_items", MoodleEffect.Read) with
        {
            MoodleCapabilities = ["moodle/site:config", "moodle/course:view"]
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };

        var result = new MoodleFunctionContractRegistry([contract])
            .Resolve(contract.FunctionName);

        Assert.True(result.IsVerified);
        Assert.Equal(contract.MoodleCapabilities, result.Contract!.MoodleCapabilities);
    }

    [Fact]
    public void Resolve_ReturnsInvalidWhenVerifiedHashDoesNotMatch()
    {
        var contract = CreateContract("local_example_get_items", MoodleEffect.Read) with { ContractHash = "not-a-real-hash" };
        var registry = new MoodleFunctionContractRegistry([contract]);

        var result = registry.Resolve(contract.FunctionName);

        Assert.Equal(MoodleContractResolutionStatus.Invalid, result.Status);
        Assert.Contains("contract_hash_invalid", result.Reasons);
    }

    [Fact]
    public void Resolve_RejectsConflictingVerifiedContracts()
    {
        var read = CreateContract("local_example_get_items", MoodleEffect.Read);
        var write = CreateContract("local_example_get_items", MoodleEffect.Write);
        var registry = new MoodleFunctionContractRegistry([read, write]);

        var result = registry.Resolve("local_example_get_items");

        Assert.Equal(MoodleContractResolutionStatus.Conflicting, result.Status);
        Assert.Contains("multiple_verified_contract_hashes", result.Reasons);
    }

    [Fact]
    public void Resolve_PrefersReleaseSpecificContractOverGenericContract()
    {
        var generic = CreateContract("local_example_get_items", MoodleEffect.Read);
        var versioned = CreateContract("local_example_get_items", MoodleEffect.Read, "4.5") with
        {
            Component = "local_example"
        };
        versioned = versioned with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(versioned) };
        var registry = new MoodleFunctionContractRegistry([generic, versioned]);

        var result = registry.Resolve("local_example_get_items", "4.5");

        Assert.True(result.IsVerified);
        Assert.Equal(versioned.ContractHash, result.ContractHash);
    }

    [Fact]
    public void SchemaValidator_PreservesRequiredNullAndNestedArraySemantics()
    {
        using var schemaDocument = JsonDocument.Parse("""
            {
              "type": "object",
              "required": ["items", "optional"],
              "properties": {
                "items": {
                  "type": "array",
                  "items": { "type": "object", "required": ["id"], "properties": { "id": { "type": "integer" } } }
                },
                "optional": { "type": ["string", "null"] }
              },
              "additionalProperties": false
            }
            """);
        var contract = CreateContract("local_example_write_items", MoodleEffect.Write) with
        {
            InputSchema = schemaDocument.RootElement.Clone()
        };

        var valid = MoodleFunctionContractSchemaValidator.ValidateInput(contract, new Dictionary<string, object?>
        {
            ["items"] = new[] { new Dictionary<string, object?> { ["id"] = 7 } },
            ["optional"] = null
        });
        var invalid = MoodleFunctionContractSchemaValidator.ValidateInput(contract, new Dictionary<string, object?>
        {
            ["items"] = new[] { new Dictionary<string, object?> { ["id"] = "7" } }
        });

        Assert.Empty(valid);
        Assert.Contains(invalid, error => error.Contains("optional", StringComparison.Ordinal));
        Assert.Contains(invalid, error => error.Contains("id", StringComparison.Ordinal));
    }

    private static MoodleFunctionContract CreateContract(string functionName, MoodleEffect effect, string? moodleVersion = null)
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = functionName,
            Effect = effect,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            MoodleVersion = moodleVersion,
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty,
            Source = "test"
        };
        return contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
    }
}
