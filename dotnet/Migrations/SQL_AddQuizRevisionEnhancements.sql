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

-- Postgres n'a pas de ADD CONSTRAINT IF NOT EXISTS : sans cette garde, relancer
-- le script (ex: après un premier passage déjà appliqué) faisait échouer toute
-- la transaction ici et annulait aussi les instructions suivantes (index,
-- colonnes Revisions) même si elles, elles n'avaient pas encore été jouées.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_Quizzes_Users_CreatedByUserId'
    ) THEN
        ALTER TABLE "Quizzes"
            ADD CONSTRAINT "FK_Quizzes_Users_CreatedByUserId"
                FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_Quizzes_CreatedByUserId" ON "Quizzes" ("CreatedByUserId");

ALTER TABLE "Revisions"
    ADD COLUMN IF NOT EXISTS "HiddenFromList" boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS "ContentFeedback" character varying(500) NULL;

CREATE INDEX IF NOT EXISTS "IX_QuizAttempts_UserId_QuizId" ON "QuizAttempts" ("UserId", "QuizId");

-- Enregistrement dans l'historique EF (convention du repo : les scripts SQL
-- manuels s'auto-enregistrent pour rendre dotnet ef database update idempotent).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251206180000_AddQuizRevisionEnhancements', '7.0.5')
ON CONFLICT DO NOTHING;

COMMIT;
