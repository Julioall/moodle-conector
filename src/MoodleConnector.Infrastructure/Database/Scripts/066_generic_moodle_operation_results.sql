ALTER TABLE moodle_pending_actions
    ADD COLUMN IF NOT EXISTS "ResultJson" jsonb,
    ADD COLUMN IF NOT EXISTS "ResultUpdatedAtUtc" timestamp with time zone;

CREATE INDEX IF NOT EXISTS "IX_moodle_pending_actions_ResultUpdatedAtUtc"
    ON moodle_pending_actions ("ResultUpdatedAtUtc");

INSERT INTO "moodle_connector_schema_versions" ("Version", "Description", "AppliedAt")
VALUES (66, 'durable generic Moodle operation results', now())
ON CONFLICT ("Version") DO NOTHING;
