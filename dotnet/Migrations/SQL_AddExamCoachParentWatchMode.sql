-- Migration: AddExamCoachParentWatchMode
-- Mode veille d'examen : un parent lié peut activer une attention renforcée
-- sur le plan ExamCoachPlan actif de son enfant. Réutilise l'entité
-- existante (ExamCoachPlan.cs) plutôt que d'en créer une nouvelle — un seul
-- champ nullable, ParentWatchModeActivatedAt (non null = veille active).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "ExamCoachPlans" ADD COLUMN IF NOT EXISTS "ParentWatchModeActivatedAt" TIMESTAMP WITH TIME ZONE NULL;

CREATE INDEX IF NOT EXISTS "IX_ExamCoachPlans_ParentWatchModeActivatedAt"
    ON "ExamCoachPlans"("ParentWatchModeActivatedAt")
    WHERE "ParentWatchModeActivatedAt" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260917000000_AddExamCoachParentWatchMode', '8.0.0')
ON CONFLICT DO NOTHING;
