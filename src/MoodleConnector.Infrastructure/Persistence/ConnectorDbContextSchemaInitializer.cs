using Microsoft.EntityFrameworkCore;

namespace MoodleConnector.Infrastructure;

public static class ConnectorDbContextSchemaInitializer
{
    private static readonly SchemaScriptPath[] SchemaScriptPaths =
    [
        new(Path.Combine("Database", "Scripts", "001_initial_schema.sql"), true),
        new(Path.Combine("Database", "Scripts", "002_openiddict_table_names.sql"), false)
    ];

    public static async Task ApplyVersionedSchemaAsync(
        this ConnectorDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        foreach (var scriptPath in SchemaScriptPaths)
        {
            var fullPath = Path.Combine(AppContext.BaseDirectory, scriptPath.RelativePath);
            if (!File.Exists(fullPath))
            {
                if (!scriptPath.Required)
                {
                    continue;
                }

                throw new FileNotFoundException($"Schema script nao encontrado: {fullPath}", fullPath);
            }

            var sql = await File.ReadAllTextAsync(fullPath, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }

    private sealed record SchemaScriptPath(string RelativePath, bool Required);
}
