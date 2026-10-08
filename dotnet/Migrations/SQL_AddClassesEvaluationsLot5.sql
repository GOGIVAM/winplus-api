-- Migration: AddClassesEvaluationsLot5
-- Lot 5  Classes, évaluations et correction IA (Modules 9, 11).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

-- Module 9 : corrigé de référence du professeur sur un devoir, en complément
-- facultatif du barème généré par WinAI (RubricJson, déjà existant).
ALTER TABLE "Assignments" ADD COLUMN IF NOT EXISTS "ReferenceAnswerText" TEXT;
ALTER TABLE "Assignments" ADD COLUMN IF NOT EXISTS "ReferenceAnswerFileUrl" VARCHAR(500);

-- Module 11 : un devoir peut référencer un quiz/épreuve du catalogue au lieu
-- d'un énoncé libre (choix structurel : réutilise le flux de soumission et
-- de correction des devoirs plutôt que d'en créer un second pour les quiz).
ALTER TABLE "Assignments" ADD COLUMN IF NOT EXISTS "QuizId" INTEGER;
DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_Assignments_Quizzes_QuizId'
    ) THEN
        ALTER TABLE "Assignments"
            ADD CONSTRAINT "FK_Assignments_Quizzes_QuizId"
            FOREIGN KEY ("QuizId") REFERENCES "Quizzes"("Id") ON DELETE SET NULL;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS "IX_Assignments_QuizId" ON "Assignments"("QuizId");

-- Module 11 : une soumission peut provenir d'une tentative de quiz assignée
-- à la classe plutôt que d'un dépôt de texte/fichier libre.
ALTER TABLE "Submissions" ADD COLUMN IF NOT EXISTS "QuizAttemptId" INTEGER;
CREATE INDEX IF NOT EXISTS "IX_Submissions_QuizAttemptId" ON "Submissions"("QuizAttemptId");

-- Module 11 : une tentative de quiz peut être rattachée à une assignation de
-- classe (NULL = jeu libre depuis le catalogue, comportement inchangé).
ALTER TABLE "QuizAttempts" ADD COLUMN IF NOT EXISTS "AssignmentId" INTEGER;
DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_QuizAttempts_Assignments_AssignmentId'
    ) THEN
        ALTER TABLE "QuizAttempts"
            ADD CONSTRAINT "FK_QuizAttempts_Assignments_AssignmentId"
            FOREIGN KEY ("AssignmentId") REFERENCES "Assignments"("Id") ON DELETE SET NULL;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS "IX_QuizAttempts_AssignmentId" ON "QuizAttempts"("AssignmentId");

-- Module 9 : détail de correction des questions ouvertes d'une tentative de
-- quiz (JSON), rempli à la soumission puis mis à jour par le professeur.
ALTER TABLE "QuizAttempts" ADD COLUMN IF NOT EXISTS "OpenGradingJson" TEXT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261006090000_AddClassesEvaluationsLot5', '8.0.0')
ON CONFLICT DO NOTHING;
