using System.Text.Json;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal static class MoodleFunctionContractManifestLoader
{
    public static IReadOnlyList<MoodleFunctionContract> Load(
        string? configuredPath,
        int maxBytes,
        bool requireVerifiedContracts = false)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            if (requireVerifiedContracts)
            {
                throw new InvalidOperationException(
                    "O modo estrito de contratos Moodle exige MoodleApi:ContractManifestPath.");
            }
            return [];
        }

        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"O manifesto de contratos Moodle configurado nao foi encontrado: {configuredPath}.");
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > Math.Clamp(maxBytes, 1024, 50 * 1024 * 1024))
        {
            throw new InvalidOperationException("O manifesto de contratos Moodle excede o limite configurado.");
        }

        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(document.RootElement, "contracts", out var contracts) ||
            contracts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("O manifesto de contratos Moodle deve conter um array 'contracts'.");
        }

        if (!TryGetProperty(document.RootElement, "schemaVersion", out var schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out var version) ||
            version != 1)
        {
            throw new InvalidOperationException("O manifesto de contratos Moodle deve declarar schemaVersion=1.");
        }

        var contractEntries = contracts.EnumerateArray().ToArray();
        if (requireVerifiedContracts)
        {
            for (var index = 0; index < contractEntries.Length; index++)
            {
                var entry = contractEntries[index];
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(
                        $"O manifesto estrito possui uma entrada de contrato invalida em contracts[{index}].");
                }

                var shapeErrors = ValidateStrictContractShape(entry);
                if (shapeErrors.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"O contrato em contracts[{index}] possui tipos invalidos: {string.Join(", ", shapeErrors)}.");
                }
            }
        }

        var parsedContracts = contractEntries
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(ParseContract)
            .ToArray();

        if (requireVerifiedContracts && parsedContracts.Length == 0)
        {
            throw new InvalidOperationException(
                "O modo estrito de contratos Moodle exige um manifesto nao vazio.");
        }

        if (requireVerifiedContracts)
        {
            var unverified = parsedContracts
                .Where(contract => contract.Status != MoodleContractStatus.Verified)
                .ToArray();
            if (unverified.Length > 0)
            {
                var sample = string.Join(", ", unverified
                    .Take(5)
                    .Select(contract => $"{contract.FunctionName}:{contract.Status.ToString().ToLowerInvariant()}"));
                throw new InvalidOperationException(
                    $"O modo estrito de contratos Moodle exige que todos os contratos sejam verificados; " +
                    $"{unverified.Length} contrato(s) nao verificado(s). Exemplos: {sample}.");
            }

            var duplicate = parsedContracts
                .GroupBy(contract => BuildIdentity(contract), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
            {
                throw new InvalidOperationException(
                    $"O manifesto estrito possui identidade de contrato duplicada: {duplicate.Key}.");
            }
        }

        if (requireVerifiedContracts)
        {
            foreach (var contract in parsedContracts.Where(contract => contract.Status == MoodleContractStatus.Verified))
            {
                var resolution = new MoodleFunctionContractRegistry([contract]).Resolve(
                    contract.FunctionName,
                    contract.MoodleVersion,
                    contract.ExternalFunctionVersion);
                if (!resolution.IsVerified)
                {
                    throw new InvalidOperationException(
                        $"O contrato verificado de '{contract.FunctionName}' nao passou a validacao de integridade: " +
                        string.Join(", ", resolution.Reasons));
                }
            }
        }

        return parsedContracts;
    }

    private static string BuildIdentity(MoodleFunctionContract contract) =>
        $"{contract.FunctionName.Trim().ToLowerInvariant()}|" +
        $"{contract.MoodleVersion?.Trim() ?? string.Empty}|" +
        $"{contract.ExternalFunctionVersion?.Trim() ?? string.Empty}";

    private static MoodleFunctionContract ParseContract(JsonElement item)
    {
        return new MoodleFunctionContract
        {
            FunctionName = GetString(item, "functionName") ?? string.Empty,
            Effect = ParseEnum<MoodleEffect>(item, "effect", MoodleEffect.Unknown),
            InputSchema = CloneOrUndefined(item, "inputSchema"),
            OutputSchema = CloneOrUndefined(item, "outputSchema"),
            MoodleVersion = GetString(item, "moodleVersion"),
            ExternalFunctionVersion = GetString(item, "externalFunctionVersion"),
            Component = GetString(item, "component"),
            PluginVersion = GetString(item, "pluginVersion"),
            Status = ParseEnum<MoodleContractStatus>(item, "status", MoodleContractStatus.Invalid),
            ContractHash = GetString(item, "contractHash") ?? string.Empty,
            Source = GetString(item, "source"),
            RequiredScopes = GetStringArray(item, "requiredScopes"),
            PlatformPermission = GetString(item, "platformPermission"),
            AdministrativeOnly = GetBoolean(item, "administrativeOnly"),
            Pagination = ParsePagination(item),
            FileHandling = ParseFileHandling(item),
            MoodleCapabilities = GetStringArray(item, "moodleCapabilities")
        };
    }

    private static IReadOnlyList<string> ValidateStrictContractShape(JsonElement item)
    {
        var errors = new List<string>();
        RequireString(item, "functionName", errors, nonEmpty: true);
        RequireString(item, "effect", errors, nonEmpty: true);
        RequireString(item, "status", errors, nonEmpty: true);
        RequireString(item, "contractHash", errors, nonEmpty: false);
        RequireObject(item, "inputSchema", errors);
        RequireObject(item, "outputSchema", errors);

        ValidateStringArray(item, "requiredScopes", errors);
        ValidateStringArray(item, "moodleCapabilities", errors);
        ValidateOptionalString(item, "moodleVersion", errors);
        ValidateOptionalString(item, "externalFunctionVersion", errors);
        ValidateOptionalString(item, "component", errors);
        ValidateOptionalString(item, "pluginVersion", errors);
        ValidateOptionalString(item, "source", errors);
        ValidateOptionalString(item, "platformPermission", errors);
        ValidateOptionalBoolean(item, "administrativeOnly", errors);
        ValidatePagination(item, errors);
        ValidateFileHandling(item, errors);
        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void RequireString(JsonElement item, string name, ICollection<string> errors, bool nonEmpty)
    {
        if (!TryGetProperty(item, name, out var value) || value.ValueKind != JsonValueKind.String ||
            (nonEmpty && string.IsNullOrWhiteSpace(value.GetString())))
        {
            errors.Add(name);
        }
    }

    private static void RequireObject(JsonElement item, string name, ICollection<string> errors)
    {
        if (!TryGetProperty(item, name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(name);
        }
    }

    private static void ValidateOptionalString(JsonElement item, string name, ICollection<string> errors)
    {
        if (TryGetProperty(item, name, out var value) &&
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            errors.Add(name);
        }
    }

    private static void ValidateOptionalBoolean(JsonElement item, string name, ICollection<string> errors)
    {
        if (TryGetProperty(item, name, out var value) &&
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add(name);
        }
    }

    private static void ValidateStringArray(JsonElement item, string name, ICollection<string> errors)
    {
        if (!TryGetProperty(item, name, out var value))
        {
            return;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Array ||
            value.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(entry.GetString())))
        {
            errors.Add(name);
        }
    }

    private static void ValidatePagination(JsonElement item, ICollection<string> errors)
    {
        if (!TryGetProperty(item, "pagination", out var pagination))
        {
            return;
        }

        if (pagination.ValueKind != JsonValueKind.Object)
        {
            errors.Add("pagination");
            return;
        }

        ValidateOptionalString(pagination, "mode", errors);
        ValidateOptionalString(pagination, "offsetParameter", errors);
        ValidateOptionalString(pagination, "limitParameter", errors);
        ValidateOptionalString(pagination, "pageParameter", errors);
        ValidateOptionalString(pagination, "pageSizeParameter", errors);
        ValidateOptionalString(pagination, "cursorParameter", errors);
        ValidateOptionalString(pagination, "hasMoreProperty", errors);
        ValidateOptionalString(pagination, "totalProperty", errors);
        ValidateOptionalString(pagination, "nextCursorProperty", errors);
    }

    private static void ValidateFileHandling(JsonElement item, ICollection<string> errors)
    {
        if (!TryGetProperty(item, "fileHandling", out var files))
        {
            return;
        }

        if (files.ValueKind != JsonValueKind.Object)
        {
            errors.Add("fileHandling");
            return;
        }

        ValidateOptionalBoolean(files, "acceptsFiles", errors);
        ValidateOptionalBoolean(files, "returnsFiles", errors);
        ValidateOptionalString(files, "destination", errors);
        ValidateStringArray(files, "allowedMimeTypes", errors);
    }

    private static MoodlePaginationContract? ParsePagination(JsonElement item)
    {
        if (!TryGetProperty(item, "pagination", out var pagination) || pagination.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new MoodlePaginationContract(
            GetString(pagination, "mode") ?? "none",
            GetString(pagination, "offsetParameter"),
            GetString(pagination, "limitParameter"),
            GetString(pagination, "pageParameter"),
            GetString(pagination, "pageSizeParameter"),
            GetString(pagination, "cursorParameter"),
            GetString(pagination, "hasMoreProperty"),
            GetString(pagination, "totalProperty"),
            GetString(pagination, "nextCursorProperty"));
    }

    private static MoodleFileHandlingContract? ParseFileHandling(JsonElement item)
    {
        if (!TryGetProperty(item, "fileHandling", out var files) || files.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new MoodleFileHandlingContract(
            GetBoolean(files, "acceptsFiles"),
            GetBoolean(files, "returnsFiles"),
            GetString(files, "destination"),
            GetStringArray(files, "allowedMimeTypes"));
    }

    private static T ParseEnum<T>(JsonElement item, string name, T fallback)
        where T : struct, Enum =>
        GetString(item, name) is { } value && Enum.TryParse<T>(value, true, out var parsed)
            ? parsed
            : fallback;

    private static JsonElement CloneOrUndefined(JsonElement item, string name) =>
        TryGetProperty(item, name, out var value) ? value.Clone() : default;

    private static string? GetString(JsonElement item, string name) =>
        TryGetProperty(item, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement item, string name) =>
        TryGetProperty(item, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static IReadOnlyList<string>? GetStringArray(JsonElement item, string name)
    {
        if (!TryGetProperty(item, name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString()!)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .ToArray();
    }

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
