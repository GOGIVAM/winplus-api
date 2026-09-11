-- Migration: AddInstitutionTeacherNetwork
-- Fusion Réseau/Mode Tuteur — affiliation prof/tuteur <-> institution (bidirectionnelle,
-- révocable des deux côtés comme TeacherStudentLinks) + demande d'accès pour contacter
-- un élève de l'institution vu en lecture seule (approuvable élève/institution/parent lié).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Voir SQL_AddTutorProfileModule.sql pour le raisonnement de l'entrée __EFMigrationsHistory.

CREATE TABLE IF NOT EXISTS "InstitutionTeacherLinks" (
    "Id"            SERIAL PRIMARY KEY,
    "InstitutionId" INTEGER NOT NULL REFERENCES "Institutions"("Id") ON DELETE CASCADE,
    "TeacherId"     INTEGER NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Status"        VARCHAR(20) NOT NULL DEFAULT 'pending',
    "InitiatedBy"   INTEGER NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT,
    "CreatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_InstitutionTeacherLinks_Institution_Teacher"
    ON "InstitutionTeacherLinks"("InstitutionId", "TeacherId");
CREATE INDEX IF NOT EXISTS "IX_InstitutionTeacherLinks_TeacherId" ON "InstitutionTeacherLinks"("TeacherId");

CREATE TABLE IF NOT EXISTS "TeacherStudentAccessRequests" (
    "Id"            SERIAL PRIMARY KEY,
    "TeacherId"     INTEGER NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "StudentId"     INTEGER NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "InstitutionId" INTEGER NOT NULL REFERENCES "Institutions"("Id") ON DELETE CASCADE,
    "Status"        VARCHAR(20) NOT NULL DEFAULT 'pending',
    "RespondedBy"   INTEGER REFERENCES "Users"("Id") ON DELETE SET NULL,
    "CreatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "RespondedAt"   TIMESTAMP WITH TIME ZONE
);

CREATE INDEX IF NOT EXISTS "IX_TeacherStudentAccessRequests_Teacher_Student_Status"
    ON "TeacherStudentAccessRequests"("TeacherId", "StudentId", "Status");
CREATE INDEX IF NOT EXISTS "IX_TeacherStudentAccessRequests_StudentId" ON "TeacherStudentAccessRequests"("StudentId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260911100000_AddInstitutionTeacherNetwork', '8.0.0')
ON CONFLICT DO NOTHING;
