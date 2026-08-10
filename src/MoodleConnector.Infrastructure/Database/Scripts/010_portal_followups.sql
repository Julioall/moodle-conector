CREATE TABLE IF NOT EXISTS portal_followups (
    "Id" uuid NOT NULL,
    "OwnerId" uuid NOT NULL,
    "StudentRef" varchar(200) NOT NULL,
    "CourseRef" varchar(200),
    "Kind" varchar(64) NOT NULL,
    "Notes" varchar(4000) NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_portal_followups" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_portal_followups_OwnerId_OccurredAt" ON portal_followups ("OwnerId", "OccurredAt");
