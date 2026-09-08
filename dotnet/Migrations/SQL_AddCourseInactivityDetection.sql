-- Migration: AddCourseInactivityDetection
-- professeur_complete.md Module 9 — Formations structurées, US-FOR-07
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "InactivityThresholdDays" INTEGER NOT NULL DEFAULT 7;

CREATE TABLE IF NOT EXISTS "CourseInactivityRelaunches" (
    "Id"       SERIAL PRIMARY KEY,
    "CourseId" INTEGER NOT NULL,
    "UserId"   INTEGER NOT NULL,
    "SentAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_CourseInactivityRelaunches_Courses_CourseId"
        FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CourseInactivityRelaunches_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_CourseInactivityRelaunches_Course_User" ON "CourseInactivityRelaunches"("CourseId", "UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260908150000_AddCourseInactivityDetection', '8.0.0')
ON CONFLICT DO NOTHING;
