using Microsoft.EntityFrameworkCore;

namespace MoodleConnector.Infrastructure;

public static class ConnectorDbContextSchemaInitializer
{
    private static readonly string[] SchemaScriptPaths =
    [
        Path.Combine("Database", "Scripts", "001_initial_schema.sql"),
        Path.Combine("Database", "Scripts", "002_openiddict_table_names.sql")
    ];

    public static async Task ApplyVersionedSchemaAsync(
        this ConnectorDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        foreach (var scriptPath in SchemaScriptPaths)
        {
            var fullPath = Path.Combine(AppContext.BaseDirectory, scriptPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Schema script nao encontrado: {fullPath}", fullPath);
            }

            var sql = await File.ReadAllTextAsync(fullPath, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }
}
