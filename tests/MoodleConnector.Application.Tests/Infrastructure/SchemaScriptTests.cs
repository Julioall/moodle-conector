using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class SchemaScriptTests
{
    [Fact]
    public async Task InitialSchemaScript_DeveSerCopiadoParaOutputEConterTabelasCriticas()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio do assembly de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "001_initial_schema.sql");

        Assert.True(File.Exists(scriptPath), $"Script de schema nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("moodle_connector_schema_versions", sql, StringComparison.Ordinal);
        Assert.Contains("connector_clients", sql, StringComparison.Ordinal);
        Assert.Contains("moodle_pending_actions", sql, StringComparison.Ordinal);
        Assert.Contains("moodle_audit_logs", sql, StringComparison.Ordinal);
        Assert.Contains("\"OpenIddictApplications\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"OpenIddictTokens\"", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }
}
