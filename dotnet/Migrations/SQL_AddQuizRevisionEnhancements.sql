-- Phase 0 du chantier "Quiz & Fiche de Révision" : colonnes additives,
-- zéro changement de comportement tant que le code applicatif ne les lit pas.
-- À exécuter manuellement en psql (convention de ce repo : beaucoup de
-- migrations n'ont pas l'attribut [Migration(...)] et sont invisibles à
-- `dotnet ef database update`).

BEGIN;

ALTER TABLE "Subjects"
    ADD COLUMN IF NOT EXISTS "Level" character varying(255) NULL;

ALTER TABLE "Quizzes"
    ADD COLUMN IF NOT EXISTS "CreatedByUserId" integer NULL,
    ADD COLUMN IF NOT EXISTS "HiddenFromList" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "DifficultyFeedback" character varying(500) NULL;

ALTER TABLE "Quizzes"
    ADD CONSTRAINT "FK_Quizzes_Users_CreatedByUserId"
        FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS "IX_Quizzes_CreatedByUserId" ON "Quizzes" ("CreatedByUserId");

ALTER TABLE "Revisions"
    ADD COLUMN IF NOT EXISTS "HiddenFromList" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "ContentFeedback" character varying(500) NULL;

CREATE INDEX IF NOT EXISTS "IX_QuizAttempts_UserId_QuizId" ON "QuizAttempts" ("UserId", "QuizId");

COMMIT;
