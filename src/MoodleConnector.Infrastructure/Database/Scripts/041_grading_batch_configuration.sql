ALTER TABLE grading_batch
    ADD COLUMN IF NOT EXISTS "TeacherInstructions" text;

ALTER TABLE grading_batch
    ADD COLUMN IF NOT EXISTS "Priority" character varying(16);

ALTER TABLE grading_batch
    ADD COLUMN IF NOT EXISTS "IncludeRubric" boolean;

ALTER TABLE grading_batch
    ADD COLUMN IF NOT EXISTS "IncludeSubmissionFiles" boolean;

ALTER TABLE grading_batch
    ADD COLUMN IF NOT EXISTS "IncludeCourseMaterials" boolean;

UPDATE grading_batch
SET "Priority" = 'normal'
WHERE "Priority" IS NULL OR btrim("Priority") = '';

UPDATE grading_batch
SET "IncludeRubric" = true
WHERE "IncludeRubric" IS NULL;

UPDATE grading_batch
SET "IncludeSubmissionFiles" = true
WHERE "IncludeSubmissionFiles" IS NULL;

UPDATE grading_batch
SET "IncludeCourseMaterials" = false
WHERE "IncludeCourseMaterials" IS NULL;

ALTER TABLE grading_batch
    ALTER COLUMN "Priority" SET DEFAULT 'normal';

ALTER TABLE grading_batch
    ALTER COLUMN "Priority" SET NOT NULL;

ALTER TABLE grading_batch
    ALTER COLUMN "IncludeRubric" SET DEFAULT true;

ALTER TABLE grading_batch
    ALTER COLUMN "IncludeRubric" SET NOT NULL;

ALTER TABLE grading_batch
    ALTER COLUMN "IncludeSubmissionFiles" SET DEFAULT true;

ALTER TABLE grading_batch
    ALTER COLUMN "IncludeSubmissionFiles" SET NOT NULL;

ALTER TABLE grading_batch
    ALTER COLUMN "IncludeCourseMaterials" SET DEFAULT false;

ALTER TABLE grading_batch
    ALTER COLUMN "IncludeCourseMaterials" SET NOT NULL;

INSERT INTO "moodle_connector_schema_versions" ("Version", "Description", "AppliedAt")
VALUES (41, 'assisted grading batch configuration', now())
ON CONFLICT ("Version") DO NOTHING;
