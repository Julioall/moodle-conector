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

    [Fact]
    public async Task GradingSchemaScript_DeveSerCopiadoParaOutputEConterTabelasDeCorrecao()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio do assembly de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "003_grading_batches.sql");

        Assert.True(File.Exists(scriptPath), $"Script de schema de correcao nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("grading_batch", sql, StringComparison.Ordinal);
        Assert.Contains("grading_item", sql, StringComparison.Ordinal);
        Assert.Contains("grading_artifact", sql, StringComparison.Ordinal);
        Assert.Contains("grading_evidence", sql, StringComparison.Ordinal);
        Assert.Contains("\"TeacherDecision\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ReviewNotes\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"PrivateNotesToTeacher\"", sql, StringComparison.Ordinal);
        Assert.Contains("IX_grading_item_BatchId_Status", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (3, 'assisted grading schema'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingAuditBatchSchemaScript_DeveSerCopiadoParaOutputEConterIndiceDeLote()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio do assembly de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "004_grading_audit_batch_index.sql");

        Assert.True(File.Exists(scriptPath), $"Script de auditoria por lote nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("BatchJobId", sql, StringComparison.Ordinal);
        Assert.Contains("IX_moodle_audit_logs_BatchJobId_CreatedAt", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (4, 'grading audit batch index'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }
}
