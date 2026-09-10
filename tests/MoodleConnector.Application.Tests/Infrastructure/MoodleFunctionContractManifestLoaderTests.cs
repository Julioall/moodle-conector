using System.Text.Json;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleFunctionContractManifestLoaderTests
{
    [Fact]
    public void Load_StrictModeRejectsMissingManifestPath()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MoodleFunctionContractManifestLoader.Load(null, 1024 * 1024, requireVerifiedContracts: true));

        Assert.Contains("ContractManifestPath", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsConfiguredManifestThatDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-moodle-contract-{Guid.NewGuid():N}.json");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024));

        Assert.Contains("nao foi encontrado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsUnknownManifestSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-version-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"schemaVersion\":2,\"contracts\":[]}");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024));

            Assert.Contains("schemaVersion=1", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StrictModeRejectsEmptyManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-empty-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"contracts\":[]}");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true));

            Assert.Contains("nao vazio", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StrictModeRejectsManifestContainingUnverifiedContracts()
    {
        var manifest = new
        {
            schemaVersion = 1,
            contracts = new[]
            {
                new
                {
                    functionName = "core_example_get_items",
                    effect = "read",
                    inputSchema = new { type = "object" },
                    outputSchema = new { type = "object" },
                    status = "missing",
                    contractHash = ""
                }
            }
        };
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-unverified-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true));

            Assert.Contains("nao verificado", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("core_example_get_items", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StrictModeRejectsMalformedStringArrayMetadata()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "core_example_create_item",
            Effect = MoodleEffect.Write,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            contracts = new[]
            {
                new
                {
                    functionName = contract.FunctionName,
                    effect = "write",
                    inputSchema = contract.InputSchema,
                    outputSchema = contract.OutputSchema,
                    status = "verified",
                    contractHash = contract.ContractHash,
                    requiredScopes = new object[] { "moodle.write", 7 }
                }
            }
        });
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-malformed-array-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, manifest);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true));

            Assert.Contains("requiredScopes", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StrictModeRejectsVerifiedContractWithInvalidHash()
    {
        var manifest = new
        {
            schemaVersion = 1,
            contracts = new[]
            {
                new
                {
                    functionName = "core_example_get_items",
                    effect = "read",
                    inputSchema = new { type = "object" },
                    outputSchema = new { type = "object" },
                    status = "verified",
                    contractHash = new string('a', 64)
                }
            }
        };
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-hash-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true));

            Assert.Contains("integridade", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StrictModeRejectsVerifiedUnknownEffect()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "core_example_unknown_effect",
            Effect = MoodleEffect.Unknown,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var manifest = new
        {
            schemaVersion = 1,
            contracts = new[]
            {
                new
                {
                    functionName = contract.FunctionName,
                    effect = "unknown",
                    inputSchema = contract.InputSchema,
                    outputSchema = contract.OutputSchema,
                    status = "verified",
                    contractHash = contract.ContractHash
                }
            }
        };
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-effect-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true));

            Assert.Contains("contract_effect_unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StrictModeRejectsDuplicateContractIdentity()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var first = new MoodleFunctionContract
        {
            FunctionName = "core_example_get_items",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            MoodleVersion = "5.1.2",
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty
        };
        first = first with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(first) };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            contracts = new[]
            {
                new
                {
                    functionName = first.FunctionName,
                    effect = "read",
                    inputSchema = first.InputSchema,
                    outputSchema = first.OutputSchema,
                    moodleVersion = first.MoodleVersion,
                    status = "verified",
                    contractHash = first.ContractHash
                },
                new
                {
                    functionName = first.FunctionName,
                    effect = "read",
                    inputSchema = first.InputSchema,
                    outputSchema = first.OutputSchema,
                    moodleVersion = first.MoodleVersion,
                    status = "verified",
                    contractHash = first.ContractHash
                }
            }
        });
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-duplicate-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, manifest);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true));

            Assert.Contains("duplicada", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ImportsAdministratorManifestWithoutExecutingItsContent()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\",\"required\":[\"courseid\"]}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "local_plugin_get_course",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            MoodleVersion = "4.5",
            ExternalFunctionVersion = "2026012100",
            Component = "local_plugin",
            AdministrativeOnly = true,
            PlatformPermission = "tool.moodle.admin.functions",
            MoodleCapabilities = ["moodle/site:config"],
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty,
            Source = "administrator-export"
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source = "administrator-export",
            contracts = new[]
            {
                new
                {
                    functionName = contract.FunctionName,
                    effect = "read",
                    inputSchema = contract.InputSchema,
                    outputSchema = contract.OutputSchema,
                    moodleVersion = contract.MoodleVersion,
                    externalFunctionVersion = contract.ExternalFunctionVersion,
                    component = contract.Component,
                    administrativeOnly = contract.AdministrativeOnly,
                    platformPermission = contract.PlatformPermission,
                    moodleCapabilities = contract.MoodleCapabilities,
                    status = "verified",
                    contractHash = contract.ContractHash,
                    source = contract.Source
                }
            }
        });
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, manifest);

        try
        {
            var loaded = MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024);
            var result = new MoodleFunctionContractRegistry(loaded)
                .Resolve(contract.FunctionName, "4.5", "2026012100");

            Assert.True(result.IsVerified);
            Assert.Equal(contract.ContractHash, result.ContractHash);
            Assert.True(result.Contract!.AdministrativeOnly);
            Assert.Equal(contract.PlatformPermission, result.Contract.PlatformPermission);
            Assert.Equal(contract.MoodleCapabilities, result.Contract.MoodleCapabilities);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_PreservesNullDefaultAndNestedArraySchemaKeywords()
    {
        using var input = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "optional": { "type": ["string", "null"], "default": null, "description": "opção" },
                "limit": { "type": "integer", "default": 25 }
              },
              "required": ["optional"],
              "additionalProperties": false
            }
            """);
        using var output = JsonDocument.Parse("""
            {
              "type": "array",
              "items": { "type": ["object", "null"] }
            }
            """);
        var contract = new MoodleFunctionContract
        {
            FunctionName = "core_example_get_items",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            contracts = new[]
            {
                new
                {
                    functionName = contract.FunctionName,
                    effect = "read",
                    inputSchema = contract.InputSchema,
                    outputSchema = contract.OutputSchema,
                    status = "verified",
                    contractHash = contract.ContractHash
                }
            }
        });
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-schema-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, manifest);

        try
        {
            var loaded = MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true);
            var imported = Assert.Single(loaded);

            var optional = imported.InputSchema.GetProperty("properties").GetProperty("optional");
            Assert.Equal(JsonValueKind.Array, optional.GetProperty("type").ValueKind);
            Assert.Contains(optional.GetProperty("type").EnumerateArray(), value => value.GetString() == "null");
            Assert.Equal(JsonValueKind.Null, optional.GetProperty("default").ValueKind);
            Assert.Equal("opção", optional.GetProperty("description").GetString());
            Assert.Equal(25, imported.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32());
            Assert.Equal(JsonValueKind.Object, imported.OutputSchema.ValueKind);
            Assert.Equal("array", imported.OutputSchema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, imported.OutputSchema.GetProperty("items").ValueKind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_StrictModeAcceptsUnspecifiedFileMimeTypesAsNull()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "mod_assign_get_submissions",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            FileHandling = new MoodleFileHandlingContract(
                AcceptsFiles: false,
                ReturnsFiles: true,
                AllowedMimeTypes: null),
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            contracts = new[]
            {
                new
                {
                    functionName = contract.FunctionName,
                    effect = "read",
                    inputSchema = contract.InputSchema,
                    outputSchema = contract.OutputSchema,
                    fileHandling = contract.FileHandling,
                    status = "verified",
                    contractHash = contract.ContractHash
                }
            }
        });
        var path = Path.Combine(Path.GetTempPath(), $"moodle-contract-file-mime-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, manifest);

        try
        {
            var loaded = MoodleFunctionContractManifestLoader.Load(path, 1024 * 1024, requireVerifiedContracts: true);
            var imported = Assert.Single(loaded);

            Assert.NotNull(imported.FileHandling);
            Assert.True(imported.FileHandling!.ReturnsFiles);
            Assert.Null(imported.FileHandling.AllowedMimeTypes);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
