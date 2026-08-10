CREATE TABLE IF NOT EXISTS portal_tasks (
    "Id" uuid NOT NULL,
    "OwnerId" uuid NOT NULL,
    "Title" varchar(240) NOT NULL,
    "Description" varchar(4000),
    "Status" varchar(32) NOT NULL,
    "Priority" varchar(32) NOT NULL,
    "DueAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_portal_tasks" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_portal_tasks_OwnerId_Status_DueAt" ON portal_tasks ("OwnerId", "Status", "DueAt");
