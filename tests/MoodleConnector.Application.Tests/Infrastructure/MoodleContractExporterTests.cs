namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleContractExporterTests
{
    [Fact]
    public async Task Exporter_resolve_o_efeito_depois_de_carregar_services_php()
    {
        var exporterPath = FindRepositoryFile("tools", "moodle-export-contract-source.php");
        var source = await File.ReadAllTextAsync(exporterPath);

        var infoIndex = source.IndexOf("external_function_info", StringComparison.Ordinal);
        var effectIndex = source.IndexOf(
            "$effect = normalize_effect($info->type ?? $row->type ?? null);",
            StringComparison.Ordinal);

        Assert.True(infoIndex >= 0, "O exportador deve consultar external_function_info().");
        Assert.True(
            effectIndex > infoIndex,
            "O efeito precisa ser obtido depois que external_function_info() carrega db/services.php.");
        Assert.Contains("$contract['requiredScopes'] = [$effect === 'write' ? 'moodle.write' : 'moodle.read'];", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exporter_tem_snapshot_de_fonte_explicitamente_nao_verificado()
    {
        var exporterPath = FindRepositoryFile("tools", "moodle-export-contract-source.php");
        var source = await File.ReadAllTextAsync(exporterPath);

        Assert.Contains("--source-only", source, StringComparison.Ordinal);
        Assert.Contains("$row->methodname = $row->methodname ?? 'execute';", source, StringComparison.Ordinal);
        Assert.Contains("--source-only cannot emit verified contracts", source, StringComparison.Ordinal);
        Assert.Contains("function discover_source_rows(): array", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Arquivo do repositorio nao encontrado: {Path.Combine(segments)}");
    }
}
