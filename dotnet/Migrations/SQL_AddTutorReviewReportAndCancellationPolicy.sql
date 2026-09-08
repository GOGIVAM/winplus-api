-- Migration: AddTutorReviewReportAndCancellationPolicy
-- Audit complet du profil répétiteur vs professeur_complete.md : signalement
-- d'avis abusif (§I "Avis vérifiés") + politique d'annulation configurable
-- (§I.C "Politique d'annulation configurable par le répétiteur").
-- Idempotent.

ALTER TABLE "TutorReviews"
    ADD COLUMN IF NOT EXISTS "IsReported" BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE "TutorReviews"
    ADD COLUMN IF NOT EXISTS "ReportReason" VARCHAR(500);
ALTER TABLE "TutorReviews"
    ADD COLUMN IF NOT EXISTS "ReportedByUserId" INTEGER;
ALTER TABLE "TutorReviews"
    ADD COLUMN IF NOT EXISTS "ReportedAt" TIMESTAMP WITH TIME ZONE;

-- Politique d'annulation configurable par le répétiteur (0 = remboursement
-- total au-delà du seuil "FullRefundHours", partiel jusqu'à "NoRefundHours",
-- rien en-deçà). Valeurs par défaut = celles décrites dans le référentiel :
-- total si >24h, partiel si <24h, aucun si <2h.
ALTER TABLE "TutorProfiles"
    ADD COLUMN IF NOT EXISTS "FullRefundHours" INTEGER NOT NULL DEFAULT 24;
ALTER TABLE "TutorProfiles"
    ADD COLUMN IF NOT EXISTS "PartialRefundPercent" INTEGER NOT NULL DEFAULT 50;
ALTER TABLE "TutorProfiles"
    ADD COLUMN IF NOT EXISTS "NoRefundHours" INTEGER NOT NULL DEFAULT 2;

ALTER TABLE "TutorBookings"
    ADD COLUMN IF NOT EXISTS "RefundPercent" INTEGER;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260908000000_AddTutorReviewReportAndCancellationPolicy', '8.0.0')
ON CONFLICT DO NOTHING;
