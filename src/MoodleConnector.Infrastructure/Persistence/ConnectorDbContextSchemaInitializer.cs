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
        ,new(Path.Combine("Database", "Scripts", "018_restore_default_read_permissions.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "020_portal_grading_permissions.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "021_portal_evidence.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "022_restore_portal_navigation_permissions.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "023_common_permission_group_keys.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "024_user_ignored_courses.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "025_structured_followups.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "026_followup_student_context.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "027_portal_task_start_at.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "028_report_jobs.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "029_report_job_excel_output.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "030_report_job_file_size.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "031_report_job_course_names.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "032_dashboard_access_snapshots.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "033_planner_links_and_external_ids.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "034_moodle_persistent_snapshots.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "035_moodle_snapshot_freshness_and_leases.sql"), true)
        ,new(Path.Combine("Database", "Scripts", "036_user_tracked_courses.sql"), true)
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

