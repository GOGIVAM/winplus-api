-- Migration: AddAlertesDecrochage
-- prompt_prof.md Module 8 — Détection de décrochage + alertes (8A)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "AlertesDecrochage" (
    "Id"            SERIAL PRIMARY KEY,
    "CourseId"      INTEGER NOT NULL,
    "StudentUserId" INTEGER NOT NULL,
    "Niveau"        VARCHAR(20) NOT NULL DEFAULT 'faible',
    "SignauxJson"   TEXT NOT NULL DEFAULT '[]',
    "DateDetection" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "Traitee"       BOOLEAN NOT NULL DEFAULT FALSE,
    "TraiteeAt"     TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_AlertesDecrochage_Courses_CourseId"
        FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AlertesDecrochage_Users_StudentUserId"
        FOREIGN KEY ("StudentUserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_AlertesDecrochage_CourseId" ON "AlertesDecrochage"("CourseId");
CREATE INDEX IF NOT EXISTS "IX_AlertesDecrochage_Traitee" ON "AlertesDecrochage"("Traitee");
-- Empêche les doublons d'alerte non traitée pour un même couple élève/formation.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AlertesDecrochage_CourseId_StudentUserId_Untreated"
    ON "AlertesDecrochage"("CourseId", "StudentUserId") WHERE "Traitee" = FALSE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907120000_AddAlertesDecrochage', '8.0.0')
ON CONFLICT DO NOTHING;
