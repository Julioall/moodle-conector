-- Keep dashboard history aligned with the canonical SENAI connection alias.
-- The guard avoids creating a duplicate daily snapshot if a newer row already
-- exists for the same owner and date under `senai`.
UPDATE dashboard_access_snapshots AS legacy
SET "ConnectionAlias" = 'senai'
WHERE lower(legacy."ConnectionAlias") = 'nacional'
  AND NOT EXISTS (
      SELECT 1
      FROM dashboard_access_snapshots AS current
      WHERE current."OwnerId" = legacy."OwnerId"
        AND current."SnapshotDate" = legacy."SnapshotDate"
        AND lower(current."ConnectionAlias") = 'senai'
  );

INSERT INTO "moodle_connector_schema_versions" ("Version", "Description", "AppliedAt")
VALUES (59, 'rename legacy nacional dashboard alias to senai', now())
ON CONFLICT ("Version") DO NOTHING;
