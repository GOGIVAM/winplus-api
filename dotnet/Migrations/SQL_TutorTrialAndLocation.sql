-- Migration: TutorTrialAndLocation
-- Tutorat (27/11/2026, validé par Georges) :
--   T7 : séance d'essai portée par la réservation (TutorBookings."IsTrial") ;
--   T8 : lieu d'une séance en présentiel (TutorBookings et TutorEngagements : "LocationAddress", "LocationNote").
-- Equivalent EF : Models/Entities/TutorBooking.cs, Models/Entities/TutorEngagement.cs.
--
-- Idempotent : rejouable sans effet. Additif : colonnes nouvelles, valeurs par défaut sans effet sur l'existant.
-- À exécuter AVANT le déploiement du code.

ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "IsTrial" BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "LocationAddress" VARCHAR(200) NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "LocationNote" VARCHAR(200) NULL;

ALTER TABLE "TutorEngagements" ADD COLUMN IF NOT EXISTS "LocationAddress" VARCHAR(200) NULL;
ALTER TABLE "TutorEngagements" ADD COLUMN IF NOT EXISTS "LocationNote" VARCHAR(200) NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261127090000_TutorTrialAndLocation', '8.0.0')
ON CONFLICT DO NOTHING;
