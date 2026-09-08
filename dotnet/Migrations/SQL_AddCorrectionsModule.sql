-- Migration: AddCorrectionsModule
-- Module 4 — Corrections (professeur_complete.md)
-- Equivalent EF entities: Assignment.cs, Submission.cs, SubmissionSimilarityDismissal.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

-- ── Assignments : devoirs donnés par un professeur à une classe ────────────
CREATE TABLE IF NOT EXISTS "Assignments" (
    "Id"              SERIAL PRIMARY KEY,
    "TeacherId"       INTEGER NOT NULL,
    "TeacherClassId"  INTEGER NOT NULL,
    "Title"           VARCHAR(200) NOT NULL,
    "StatementText"   TEXT,
    "RubricJson"      TEXT,
    "MaxScore"        NUMERIC(5,2) NOT NULL DEFAULT 20,
    "DueDate"         TIMESTAMP WITH TIME ZONE,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_Assignments_Users_TeacherId"
        FOREIGN KEY ("TeacherId") REFERENCES "Users"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Assignments_TeacherClasses_TeacherClassId"
        FOREIGN KEY ("TeacherClassId") REFERENCES "TeacherClasses"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_Assignments_TeacherId" ON "Assignments"("TeacherId");
CREATE INDEX IF NOT EXISTS "IX_Assignments_TeacherClassId" ON "Assignments"("TeacherClassId");

-- ── Submissions : copie d'un élève + sa correction (une seule à la fois) ──
CREATE TABLE IF NOT EXISTS "Submissions" (
    "Id"                SERIAL PRIMARY KEY,
    "AssignmentId"      INTEGER NOT NULL,
    "StudentId"         INTEGER NOT NULL,
    "Content"           TEXT,
    "FileUrl"           VARCHAR(500),
    "Source"            VARCHAR(20) NOT NULL DEFAULT 'student',
    "SubmittedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "Status"            VARCHAR(20) NOT NULL DEFAULT 'pending',
    "Score"             NUMERIC(5,2),
    "Comment"           TEXT,
    "ErrorType"         VARCHAR(30),
    "DraftUpdatedAt"    TIMESTAMP WITH TIME ZONE,
    "GradedAt"          TIMESTAMP WITH TIME ZONE,
    "GradedByUserId"    INTEGER,
    CONSTRAINT "FK_Submissions_Assignments_AssignmentId"
        FOREIGN KEY ("AssignmentId") REFERENCES "Assignments"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Submissions_Users_StudentId"
        FOREIGN KEY ("StudentId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Submissions_AssignmentId_StudentId" ON "Submissions"("AssignmentId", "StudentId");
CREATE INDEX IF NOT EXISTS "IX_Submissions_Status" ON "Submissions"("Status");

-- ── SubmissionSimilarityDismissals : alertes de similarité ignorées ────────
CREATE TABLE IF NOT EXISTS "SubmissionSimilarityDismissals" (
    "Id"                  SERIAL PRIMARY KEY,
    "SubmissionAId"       INTEGER NOT NULL,
    "SubmissionBId"       INTEGER NOT NULL,
    "DismissedByUserId"   INTEGER NOT NULL,
    "DismissedAt"         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SubmissionSimilarityDismissals_Pair"
    ON "SubmissionSimilarityDismissals"("SubmissionAId", "SubmissionBId");

-- ── Historique EF : voir SQL_AddTutorProfileModule.sql pour le raisonnement.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260906120000_AddCorrectionsModule', '8.0.0')
ON CONFLICT DO NOTHING;
