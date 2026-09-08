-- Migration: AddTutorInsightsModule
-- Module 6 — Cours particuliers (professeur_complete.md)
-- US-REP-11 (fiche de révision élève), US-REP-12 (rapport mensuel de coaching)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "TutorRevisionSheets" (
    "Id"            SERIAL PRIMARY KEY,
    "TutorUserId"   INTEGER NOT NULL,
    "StudentUserId" INTEGER NOT NULL,
    "Subject"       VARCHAR(100),
    "Content"       TEXT NOT NULL,
    "GeneratedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"     TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_TutorRevisionSheets_Users_TutorUserId"
        FOREIGN KEY ("TutorUserId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorRevisionSheets_Users_StudentUserId"
        FOREIGN KEY ("StudentUserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorRevisionSheets_Tutor_Student"
    ON "TutorRevisionSheets"("TutorUserId", "StudentUserId");

CREATE TABLE IF NOT EXISTS "TutorCoachingReports" (
    "Id"          SERIAL PRIMARY KEY,
    "TutorUserId" INTEGER NOT NULL,
    "MonthLabel"  VARCHAR(50) NOT NULL,
    "Content"     TEXT NOT NULL,
    "GeneratedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_TutorCoachingReports_Users_TutorUserId"
        FOREIGN KEY ("TutorUserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_TutorCoachingReports_Tutor_GeneratedAt"
    ON "TutorCoachingReports"("TutorUserId", "GeneratedAt" DESC);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907193000_AddTutorInsightsModule', '8.0.0')
ON CONFLICT DO NOTHING;
