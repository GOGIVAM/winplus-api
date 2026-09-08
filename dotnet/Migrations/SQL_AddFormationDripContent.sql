-- Migration: AddFormationDripContent
-- prompt_prof.md Module 5B — Drip content et checkpoints vidéo (Formations)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "CourseSections" ADD COLUMN IF NOT EXISTS "UnlockRule" VARCHAR(20) NOT NULL DEFAULT 'immediate';
ALTER TABLE "CourseSections" ADD COLUMN IF NOT EXISTS "DelayDays" INTEGER;
ALTER TABLE "CourseSections" ADD COLUMN IF NOT EXISTS "MinScore" INTEGER;

ALTER TABLE "CourseLessons" ADD COLUMN IF NOT EXISTS "CheckpointsJson" TEXT;

CREATE TABLE IF NOT EXISTS "SectionUnlockNotifications" (
    "Id"         SERIAL PRIMARY KEY,
    "UserId"     INTEGER NOT NULL,
    "SectionId"  INTEGER NOT NULL,
    "NotifiedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_SectionUnlockNotifications_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SectionUnlockNotifications_CourseSections_SectionId"
        FOREIGN KEY ("SectionId") REFERENCES "CourseSections"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_SectionUnlockNotifications_User_Section"
    ON "SectionUnlockNotifications"("UserId", "SectionId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260908090000_AddFormationDripContent', '8.0.0')
ON CONFLICT DO NOTHING;
