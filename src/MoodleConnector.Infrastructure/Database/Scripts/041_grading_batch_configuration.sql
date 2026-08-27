ALTER TABLE grading_batch
    ADD COLUMN IF NOT EXISTS "TeacherInstructions" text;

ALTER TABLE grading_batch
    ADD COLUMN IF NOT EXISTS "Priority" character varying(16);

UPDATE grading_batch
SET "Priority" = 'normal'
WHERE "Priority" IS NULL OR btrim("Priority") = '';

ALTER TABLE grading_batch
    ALTER COLUMN "Priority" SET DEFAULT 'normal';

ALTER TABLE grading_batch
    ALTER COLUMN "Priority" SET NOT NULL;

INSERT INTO "moodle_connector_schema_versions" ("Version", "Description", "AppliedAt")
VALUES (41, 'assisted grading batch configuration', now())
ON CONFLICT ("Version") DO NOTHING;
