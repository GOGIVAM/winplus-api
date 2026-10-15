-- Migration: Subscriptions97bBranchement
-- Lot 9.7b, étape 2 (suivi 18.15, 18.17, 18.21) : branchement des contrôles
-- sur les droits typés.
--   * AiTokenUsages.ReferenceTokens : référence WinAI totale (droits + bonus)
--     inscrite par .NET sur chaque réserve, relue par le contrôle Python
--     (ai_quota.py) à la place de la grille de plans dupliquée, supprimée ;
--   * TutorBookings.TeacherShareAtPayment et SessionEnrollments.TeacherShareAtPayment :
--     part du professeur figée au paiement (0.70 à 0.80).
-- Equivalent EF : Models/Entities/AiTokenUsage.cs, TutorBooking.cs, SessionEnrollment.cs.
--
-- Aucun prérequis d'ordre : ne dépend que de tables historiques ; peut passer
-- avant ou après n'importe quel autre script. Idempotent. Additif : aucune
-- donnée existante modifiée (la part des séances déjà payées est figée à leur
-- prochaine synchronisation du registre, à la part du plan en vigueur).
-- À exécuter AVANT le déploiement du code (le modèle EF lit ces colonnes).

ALTER TABLE "AiTokenUsages"      ADD COLUMN IF NOT EXISTS "ReferenceTokens"       BIGINT        NULL;
ALTER TABLE "TutorBookings"      ADD COLUMN IF NOT EXISTS "TeacherShareAtPayment" NUMERIC(5,4)  NULL;
ALTER TABLE "SessionEnrollments" ADD COLUMN IF NOT EXISTS "TeacherShareAtPayment" NUMERIC(5,4)  NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_TutorBookings_TeacherShareAtPayment') THEN
        ALTER TABLE "TutorBookings" ADD CONSTRAINT "CK_TutorBookings_TeacherShareAtPayment"
            CHECK ("TeacherShareAtPayment" IS NULL OR ("TeacherShareAtPayment" >= 0 AND "TeacherShareAtPayment" <= 1));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_SessionEnrollments_TeacherShareAtPayment') THEN
        ALTER TABLE "SessionEnrollments" ADD CONSTRAINT "CK_SessionEnrollments_TeacherShareAtPayment"
            CHECK ("TeacherShareAtPayment" IS NULL OR ("TeacherShareAtPayment" >= 0 AND "TeacherShareAtPayment" <= 1));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261015130000_Subscriptions97bBranchement', '8.0.0')
ON CONFLICT DO NOTHING;
