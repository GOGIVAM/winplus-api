-- Migration: AddTutorBookingLifecycleAndReviews
-- Module 6 — Cycle de vie complet de la réservation (accept/refuse/expiration/
-- escrow simulé) + module Avis (professeur_complete.md, US-REP-05..08).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "CompletedAt" TIMESTAMP WITH TIME ZONE;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "EscrowReleasedAt" TIMESTAMP WITH TIME ZONE;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "DisputedAt" TIMESTAMP WITH TIME ZONE;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "DisputeReason" VARCHAR(500);

-- ── TutorReviews : avis élève sur une séance effectuée (US-REP-08) ─────────
CREATE TABLE IF NOT EXISTS "TutorReviews" (
    "Id"               SERIAL PRIMARY KEY,
    "TutorBookingId"   INTEGER NOT NULL,
    "TutorProfileId"   INTEGER NOT NULL,
    "StudentUserId"    INTEGER NOT NULL,
    "Rating"           INTEGER NOT NULL,
    "Comment"          VARCHAR(300),
    "TutorReply"       VARCHAR(500),
    "TutorRepliedAt"   TIMESTAMP WITH TIME ZONE,
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_TutorReviews_TutorBookings_TutorBookingId"
        FOREIGN KEY ("TutorBookingId") REFERENCES "TutorBookings"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorReviews_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorReviews_Users_StudentUserId"
        FOREIGN KEY ("StudentUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorReviews_TutorBookingId" ON "TutorReviews"("TutorBookingId");
CREATE INDEX IF NOT EXISTS "IX_TutorReviews_TutorProfileId" ON "TutorReviews"("TutorProfileId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907150000_AddTutorBookingLifecycleAndReviews', '7.0.5')
ON CONFLICT DO NOTHING;
