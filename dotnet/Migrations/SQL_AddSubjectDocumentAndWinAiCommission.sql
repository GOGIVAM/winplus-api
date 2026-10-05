-- Migration: AddSubjectDocumentAndWinAiCommission
-- Module 8 : "Subjects" n'avait aucune colonne pour recevoir l'adresse du
-- fichier déposé à la création  le flux de publication professeur
-- (ContentPublishFlow.tsx) envoyait un fichier que le contrôleur ne pouvait
-- nulle part persister. Ajoute DocumentUrl.
-- Module 7 : commission catalogue bornée 10%-60% selon un score WinAI,
-- figée au contenu au moment de l'évaluation (jamais recalculée a
-- posteriori sur les ventes déjà enregistrées).
-- Equivalent EF entity: Models/Entities/Subject.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Subjects" ADD COLUMN IF NOT EXISTS "DocumentUrl" VARCHAR(1000);
ALTER TABLE "Subjects" ADD COLUMN IF NOT EXISTS "WinAiScore" NUMERIC(5,2);
ALTER TABLE "Subjects" ADD COLUMN IF NOT EXISTS "WinAiJustification" TEXT;
ALTER TABLE "Subjects" ADD COLUMN IF NOT EXISTS "PlatformCommissionRate" NUMERIC(5,4);
ALTER TABLE "Subjects" ADD COLUMN IF NOT EXISTS "WinAiScoreEvaluatedAt" TIMESTAMP WITH TIME ZONE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005120000_AddSubjectDocumentAndWinAiCommission', '8.0.0')
ON CONFLICT DO NOTHING;
