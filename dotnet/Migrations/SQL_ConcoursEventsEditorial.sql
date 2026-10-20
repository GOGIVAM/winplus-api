-- Migration: ConcoursEventsEditorial
-- Lot 10.4 : contenus éditoriaux des pages concours, saisis dans l'écran admin
-- « Contenus des concours » (AdminConcoursEventsController).
--   * Programme, AdmissionConditions, Tracks (filières), Coefficients : texte
--     simple ou Markdown léger ;
--   * SourceUrl (une URL par ligne) et SourceCheckedAt (date de consultation) :
--     obligatoires dès qu'une information officielle est saisie (programme,
--     conditions, coefficients, dates), contrôle fait par l'API ;
--   * versions anglaises facultatives, même structure avec le suffixe En
--     (NameEn, NotesEn, TipsEn, FaqJsonEn, ProgrammeEn, AdmissionConditionsEn,
--     TracksEn, CoefficientsEn).
-- Equivalent EF : Models/Entities/ConcoursEvent.cs.
--
-- Prérequis : table ConcoursEvents. Les colonnes Tips et FaqJson (Module 2) sont
-- ajoutées ici aussi si elles manquent, pour ne dépendre d'aucun ordre.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "Tips"                  VARCHAR(2000) NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "FaqJson"               TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "Programme"             TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "AdmissionConditions"   TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "Tracks"                TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "Coefficients"          TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "SourceUrl"             VARCHAR(1000) NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "SourceCheckedAt"       TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "NameEn"                VARCHAR(200)  NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "NotesEn"               VARCHAR(1000) NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "TipsEn"                VARCHAR(2000) NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "FaqJsonEn"             TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "ProgrammeEn"           TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "AdmissionConditionsEn" TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "TracksEn"              TEXT          NULL;
ALTER TABLE "ConcoursEvents" ADD COLUMN IF NOT EXISTS "CoefficientsEn"        TEXT          NULL;

CREATE INDEX IF NOT EXISTS "IX_ConcoursEvents_Slug_Year" ON "ConcoursEvents"("Slug", "Year");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261020120000_ConcoursEventsEditorial', '8.0.0')
ON CONFLICT DO NOTHING;
