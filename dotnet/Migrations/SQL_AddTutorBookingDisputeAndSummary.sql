-- Migration: AddTutorBookingDisputeAndSummary
-- Module 6 — Cours particuliers (professeur_complete.md)
-- US-REP-07 (compte-rendu WinAI), US-REP-09 (résolution de litige), US-REP-10 (matière par réservation)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "Subject" VARCHAR(100);
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "TranscriptText" TEXT;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "SummaryText" TEXT;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "DisputeResolution" VARCHAR(30);
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "DisputeResolutionNote" VARCHAR(500);
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "DisputeResolvedByUserId" INTEGER;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "DisputeResolvedAt" TIMESTAMP WITH TIME ZONE;

CREATE INDEX IF NOT EXISTS "IX_TutorBookings_Status" ON "TutorBookings"("Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907190000_AddTutorBookingDisputeAndSummary', '8.0.0')
ON CONFLICT DO NOTHING;
