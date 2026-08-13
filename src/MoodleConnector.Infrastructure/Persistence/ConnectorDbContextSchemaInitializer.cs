using Microsoft.EntityFrameworkCore;

namespace MoodleConnector.Infrastructure;

public static class ConnectorDbContextSchemaInitializer
{
    private static readonly SchemaScriptPath[] SchemaScriptPaths =
    [
        new(Path.Combine("Database", "Scripts", "001_initial_schema.sql"), true),
        new(Path.Combine("Database", "Scripts", "002_openiddict_table_names.sql"), false),
        new(Path.Combine("Database", "Scripts", "003_grading_batches.sql"), true),
        new(Path.Combine("Database", "Scripts", "004_grading_audit_batch_index.sql"), true),
        new(Path.Combine("Database", "Scripts", "005_user_memories.sql"), true),
        new(Path.Combine("Database", "Scripts", "006_user_memory_documents.sql"), true),
        new(Path.Combine("Database", "Scripts", "007_universal_moodle_audit_fields.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "008_portal_tasks.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "009_portal_calendar_events.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "010_portal_followups.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "011_moodle_connection_validation.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "012_team_scoped_access.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "013_platform_permission_groups.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "014_backfill_platform_tool_permissions.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "015_grant_all_platform_permissions.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "016_permission_group_management.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "017_revoke_temporary_global_permissions.sql"), true)
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

