-- Migration: TutorInPersonVerificationDelivery
-- Lot 9.5, arbitrages de livraison (suivi Partie 17.17, décisions du 2026-10-13) :
--   * un seul avis par institution payeuse et par séance, quel que soit le
--     compte de l'institution qui note (colonne ReviewerInstitutionId + index
--     unique partiel) ;
--   * fichiers d'identité d'un compte supprimé : la suppression en cascade
--     Users -> TutorProfiles -> TutorIdentityChecks ferait perdre les
--     références des fichiers privés, qui ne seraient jamais purgés. Un
--     déclencheur garde ces références (TutorIdentityFilePurges) AVANT la
--     suppression de la ligne ; la tâche de purge existante les supprime à
--     l'échéance normale (30 jours après validation, 7 jours après refus,
--     tout de suite pour un dépôt jamais décidé).
-- Equivalent EF : Models/Entities/TutorReview.cs (ReviewerInstitutionId),
-- Models/Entities/TutorVerification.cs (TutorIdentityFilePurge),
-- Data/ApplicationDbContext.cs (index UX_TutorReviews_Booking_Institution).
--
-- PRÉREQUIS : SQL_TutorInPersonVerificationAndReports.sql (lot 9.5) déjà passé
-- (tables TutorIdentityChecks et colonne TutorReviews.ReviewerRole).
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée ; seules les lignes d'avis
-- d'institution déjà existantes reçoivent leur institution (au plus une par
-- séance et par institution : la plus ancienne).

-- ── Avis : un seul par institution payeuse et par séance ─────────────────
ALTER TABLE "TutorReviews" ADD COLUMN IF NOT EXISTS "ReviewerInstitutionId" INTEGER NULL;

-- Avis déjà donnés par un compte institution pour une séance payée par son
-- institution : rattachés à l'institution (le plus ancien seulement, pour que
-- l'index unique ci-dessous puisse être créé même avec d'anciens doublons).
UPDATE "TutorReviews" r
   SET "ReviewerInstitutionId" = x."InstitutionId"
  FROM (
        SELECT r2."Id", u."InstitutionId",
               ROW_NUMBER() OVER (PARTITION BY r2."TutorBookingId", u."InstitutionId" ORDER BY r2."CreatedAt", r2."Id") AS rn
          FROM "TutorReviews" r2
          JOIN "Users" u         ON u."Id" = r2."StudentUserId" AND u."Role" = 'institution' AND u."InstitutionId" IS NOT NULL
          JOIN "TutorBookings" b ON b."Id" = r2."TutorBookingId" AND b."PayerInstitutionId" = u."InstitutionId"
         WHERE NOT EXISTS (SELECT 1 FROM "TutorReviews" r3
                            WHERE r3."TutorBookingId" = r2."TutorBookingId"
                              AND r3."ReviewerInstitutionId" = u."InstitutionId")
       ) x
 WHERE r."Id" = x."Id" AND x.rn = 1 AND r."ReviewerInstitutionId" IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_TutorReviews_Booking_Institution"
    ON "TutorReviews"("TutorBookingId", "ReviewerInstitutionId") WHERE "ReviewerInstitutionId" IS NOT NULL;

-- ── Fichiers d'identité d'un compte supprimé : références gardées pour la purge ──
CREATE TABLE IF NOT EXISTS "TutorIdentityFilePurges" (
    "Id"                   SERIAL PRIMARY KEY,
    "FileRef"              VARCHAR(500) NOT NULL,
    "TutorIdentityCheckId" INTEGER      NULL,
    "Status"               VARCHAR(20)  NOT NULL DEFAULT 'pending',
    "ReviewedAt"           TIMESTAMP WITH TIME ZONE NULL,
    "QueuedAt"             TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorIdentityFilePurges_FileRef" ON "TutorIdentityFilePurges"("FileRef");

CREATE OR REPLACE FUNCTION "fn_TutorIdentityChecks_QueueFilePurge"() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD."FilesPurgedAt" IS NULL THEN
        INSERT INTO "TutorIdentityFilePurges" ("FileRef", "TutorIdentityCheckId", "Status", "ReviewedAt", "QueuedAt")
        SELECT f.ref, OLD."Id", OLD."Status", OLD."ReviewedAt", NOW()
          FROM unnest(ARRAY[OLD."FrontFileRef", OLD."BackFileRef", OLD."SelfieFileRef"]) AS f(ref)
         WHERE f.ref IS NOT NULL
        ON CONFLICT ("FileRef") DO NOTHING;
    END IF;
    RETURN OLD;
END;
$$;

DROP TRIGGER IF EXISTS "TRG_TutorIdentityChecks_QueueFilePurge" ON "TutorIdentityChecks";
CREATE TRIGGER "TRG_TutorIdentityChecks_QueueFilePurge"
    BEFORE DELETE ON "TutorIdentityChecks"
    FOR EACH ROW EXECUTE PROCEDURE "fn_TutorIdentityChecks_QueueFilePurge"();

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261013120000_TutorInPersonVerificationDelivery', '8.0.0')
ON CONFLICT DO NOTHING;
