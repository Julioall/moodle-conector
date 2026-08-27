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

    [Fact]
    public async Task UserMemoriesSchemaScript_DeveSerCopiadoParaOutputEConterTabelaRestricoesEIndices()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio do assembly de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "005_user_memories.sql");

        Assert.True(File.Exists(scriptPath), $"Script de memorias de usuario nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("user_memories", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (\"Category\" IN ('preferencia', 'caminho', 'correcao', 'decisao', 'modelo'))", sql, StringComparison.Ordinal);
        Assert.Contains("\"LinkedDocumentId\" uuid NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (\"Origin\" IN ('explicit', 'inferred'))", sql, StringComparison.Ordinal);
        Assert.Contains("IX_user_memories_OwnerSubject_MoodleAlias_CourseId_UpdatedAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULLS NOT DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"NormalizedKey\") NULLS NOT DISTINCT;", sql, StringComparison.Ordinal);
        Assert.Contains("(\"Version\", \"Description\", \"AppliedAt\")", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (5, 'user memories'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserMemoryDocumentsSchemaScript_DeveSerCopiadoParaOutputEConterTabelaRestricoesEIndices()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio do assembly de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "006_user_memory_documents.sql");

        Assert.True(File.Exists(scriptPath), $"Script de documentos de memoria nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("user_memory_documents", sql, StringComparison.Ordinal);
        Assert.Contains("\"Content\" varchar(200000) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (\"Format\" IN ('markdown', 'html', 'text'))", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (\"Origin\" IN ('explicit', 'inferred'))", sql, StringComparison.Ordinal);
        Assert.Contains("IX_user_memory_documents_OwnerSubject_MoodleAlias_CourseId_UpdatedAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("NULLS NOT DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALUES (6, 'user memory documents'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DashboardAccessSnapshotsSchemaScript_DeveConterIndiceDiarioEAgregados()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "032_dashboard_access_snapshots.sql");

        Assert.True(File.Exists(scriptPath), $"Script de snapshots nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("dashboard_access_snapshots", sql, StringComparison.Ordinal);
        Assert.Contains("SnapshotDate", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VALUES (32, 'daily dashboard access and risk snapshots'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlannerLinksSchemaScript_DeveConterTagsEIdsExternos()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "033_planner_links_and_external_ids.sql");

        Assert.True(File.Exists(scriptPath), $"Script de vínculos nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("planner_links", sql, StringComparison.Ordinal);
        Assert.Contains("ReferenceType", sql, StringComparison.Ordinal);
        Assert.Contains("ExternalUid", sql, StringComparison.Ordinal);
        Assert.Contains("moodle_connector_schema_versions", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (33, 'planner links and calendar external ids'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistentSnapshotsSchemaScript_DeveConterSnapshotsEstadosEIndiceUnico()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "034_moodle_persistent_snapshots.sql");

        Assert.True(File.Exists(scriptPath), $"Script de snapshots persistentes nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("moodle_snapshots", sql, StringComparison.Ordinal);
        Assert.Contains("moodle_sync_states", sql, StringComparison.Ordinal);
        Assert.Contains("PayloadJson", sql, StringComparison.Ordinal);
        Assert.Contains("SnapshotType", sql, StringComparison.Ordinal);
        Assert.Contains("IX_moodle_snapshots_scope", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (34, 'persistent Moodle snapshots and sync state'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotFreshnessSchemaScript_DeveConterPrazosELeasesDuraveis()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "035_moodle_snapshot_freshness_and_leases.sql");

        Assert.True(File.Exists(scriptPath), $"Script de freshness nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("FreshUntil", sql, StringComparison.Ordinal);
        Assert.Contains("StaleUntil", sql, StringComparison.Ordinal);
        Assert.Contains("LeaseUntil", sql, StringComparison.Ordinal);
        Assert.Contains("AttemptCount", sql, StringComparison.Ordinal);
        Assert.Contains("IX_moodle_sync_states_due", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (35, 'snapshot freshness metadata and durable sync leases'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrackedCoursesSchemaScript_DeveConterPreferenciasDeCursosAcompanhados()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "036_user_tracked_courses.sql");

        Assert.True(File.Exists(scriptPath), $"Script de cursos acompanhados nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("user_tracked_courses", sql, StringComparison.Ordinal);
        Assert.Contains("IX_user_tracked_courses_OwnerId_ConnectionAlias_CourseId", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (36, 'explicitly tracked course preferences'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotIdentityAndRunsSchemaScript_DeveSerExpansivoEIdempotente()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "037_moodle_snapshot_connection_identity_and_runs.sql");

        Assert.True(File.Exists(scriptPath), $"Script de identidade dos snapshots nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("ConnectionId", sql, StringComparison.Ordinal);
        Assert.Contains("moodle_snapshot_runs", sql, StringComparison.Ordinal);
        Assert.Contains("moodle_snapshot_run_items", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE \"ConnectionId\" <> ''", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (37, 'stable snapshot connection identity and technical synchronization runs'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotLineageAndMigrationAuditScripts_DevemConterHeadEOrfaos()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");

        var lineage = await File.ReadAllTextAsync(Path.Combine(
            assemblyDirectory, "Database", "Scripts", "038_moodle_snapshot_run_lineage.sql"));
        Assert.Contains("LastRunId", lineage, StringComparison.Ordinal);
        Assert.Contains("VALUES (38, 'snapshot head lineage to technical run'", lineage, StringComparison.Ordinal);

        var audit = await File.ReadAllTextAsync(Path.Combine(
            assemblyDirectory, "Database", "Scripts", "039_moodle_snapshot_identity_migration_audit.sql"));
        Assert.Contains("moodle_snapshot_identity_migration_issues", audit, StringComparison.Ordinal);
        Assert.Contains("ambiguous_active_alias", audit, StringComparison.Ordinal);
        Assert.Contains("snapshot_orphan_connection", audit, StringComparison.Ordinal);
        Assert.Contains("sync_state_orphan_connection", audit, StringComparison.Ordinal);
        Assert.Contains("VALUES (39, 'snapshot identity migration audit view'", audit, StringComparison.Ordinal);

        var worker = await File.ReadAllTextAsync(Path.Combine(
            assemblyDirectory, "Database", "Scripts", "040_moodle_snapshot_worker_id.sql"));
        Assert.Contains("WorkerId", worker, StringComparison.Ordinal);
        Assert.Contains("VALUES (40, 'snapshot worker instance lineage'", worker, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssistedGradingBatchConfigurationScript_DeveSerAditivoEConterCamposDeLease()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(
            assemblyDirectory,
            "Database",
            "Scripts",
            "041_grading_batch_configuration.sql");

        Assert.True(File.Exists(scriptPath), $"Script de configuracao de lote nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("ADD COLUMN IF NOT EXISTS \"TeacherInstructions\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"IncludeRubric\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"LeaseOwner\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"CheckpointItemId\"", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (41, 'assisted grading batch configuration'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingContextIdentityScript_DeveSerAditivoEIdempotente()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(
            assemblyDirectory,
            "Database",
            "Scripts",
            "042_grading_context_identity.sql");

        Assert.True(File.Exists(scriptPath), $"Script de identidade de contexto nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("ADD COLUMN IF NOT EXISTS \"ContextVersion\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"ContextHash\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"ContextStatus\"", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (42, 'canonical grading context identity'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingContextSnapshotScript_DevePersistirPayloadAppendOnly()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(
            assemblyDirectory,
            "Database",
            "Scripts",
            "043_grading_context_snapshots.sql");

        Assert.True(File.Exists(scriptPath), $"Script de snapshots de contexto nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("CREATE TABLE IF NOT EXISTS grading_context_snapshot", sql, StringComparison.Ordinal);
        Assert.Contains("\"PayloadJson\" jsonb NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"Version\" > 0", sql, StringComparison.Ordinal);
        Assert.Contains("IX_grading_context_snapshot_Item_Version", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (43, 'canonical grading context snapshots'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingItemLeaseScript_DeveSerAditivoEConterClaimIdempotente()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(
            assemblyDirectory,
            "Database",
            "Scripts",
            "044_grading_item_leases.sql");

        Assert.True(File.Exists(scriptPath), $"Script de lease por item nao encontrado em {scriptPath}.");

        var sql = await File.ReadAllTextAsync(scriptPath);

        Assert.Contains("ADD COLUMN IF NOT EXISTS \"LeaseOwner\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"LeaseUntil\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"AttemptCount\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"NextAttemptAt\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"LastErrorCode\"", sql, StringComparison.Ordinal);
        Assert.Contains("IX_grading_item_JobClaim", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (44, 'assisted grading item leases'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingAiProposalScript_DevePersistirVersaoHashEConfidenceComLimites()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(
            assemblyDirectory,
            "Database",
            "Scripts",
            "045_grading_ai_proposals.sql");

        Assert.True(File.Exists(scriptPath), $"Script de proposta IA nao encontrado em {scriptPath}.");
        var sql = await File.ReadAllTextAsync(scriptPath);
        Assert.Contains("CREATE TABLE IF NOT EXISTS grading_ai_proposal", sql, StringComparison.Ordinal);
        Assert.Contains("\"ProposalHash\" character varying(64) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"Confidence\" >= 0 AND \"Confidence\" <= 1", sql, StringComparison.Ordinal);
        Assert.Contains("IX_grading_ai_proposal_Item_Version", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (45, 'versioned assisted grading AI proposals'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingItemProcessingStagesScript_DeveSerAditivoEIdempotente()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "046_grading_item_processing_stages.sql");
        Assert.True(File.Exists(scriptPath));
        var sql = await File.ReadAllTextAsync(scriptPath);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"ProcessingStage\"", sql, StringComparison.Ordinal);
        Assert.Contains("ProcessingStageUpdatedAt", sql, StringComparison.Ordinal);
        Assert.Contains("CK_grading_item_processing_stage_known", sql, StringComparison.Ordinal);
        Assert.Contains("IX_grading_item_ProcessingStage", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (46, 'assisted grading item processing checkpoints'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingBatchExecutionContextScript_DevePersistirIdentidadeNaoSecreta()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "047_grading_batch_execution_context.sql");
        Assert.True(File.Exists(scriptPath));
        var sql = await File.ReadAllTextAsync(scriptPath);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"ConnectorClientId\"", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"ConnectionAlias\"", sql, StringComparison.Ordinal);
        Assert.Contains("IX_grading_batch_ExecutionContext", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (47, 'durable grading batch execution context'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GradingArtifactDeferredSourceScript_DevePersistirReferenciaSemToken()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)
            ?? throw new InvalidOperationException("Diretorio de infraestrutura nao encontrado.");
        var scriptPath = Path.Combine(assemblyDirectory, "Database", "Scripts", "048_grading_artifact_deferred_source.sql");
        Assert.True(File.Exists(scriptPath));
        var sql = await File.ReadAllTextAsync(scriptPath);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"SourceUrl\"", sql, StringComparison.Ordinal);
        Assert.Contains("VALUES (48, 'deferred grading artifact source references'", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
    }
}
