-- Migration: InstitutionWalletHolderAndAttachmentConsent
-- Arbitrages du 9.3 (suivi 17.14, décisions du 2026-10-12) :
--   - consentement de l'élève à son rattachement à une institution
--     (InstitutionStudents.ConfirmationStatus : pending | accepted | refused),
--     exigé pour le tutorat seulement ;
--   - trace des changements de titulaire du portefeuille institution
--     (InstitutionWalletHolderChanges), décidés par un administrateur.
-- Les retraits interdits aux portefeuilles élève, parent et institution ne
-- demandent aucune modification de schéma (règle serveur).
-- Equivalent EF : Models/Entities/InstitutionStudent.cs,
-- InstitutionWalletHolderChange.cs, Data/ApplicationDbContext.cs.
-- Prérequis : SQL_TutorPayersParticipantsGroupsAndAssignments.sql (lot 9.3).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée.

-- ── InstitutionStudents : confirmation par l'élève ─────────────────────────
-- Les rattachements existants passent en « en attente » (DEFAULT appliqué
-- aux lignes existantes à l'ajout de la colonne). Rejouer le script ne
-- touche plus aux réponses déjà données : la colonne existe alors déjà.
ALTER TABLE "InstitutionStudents" ADD COLUMN IF NOT EXISTS "ConfirmationStatus" VARCHAR(20) NOT NULL DEFAULT 'pending';
ALTER TABLE "InstitutionStudents" ADD COLUMN IF NOT EXISTS "RespondedAt" TIMESTAMP WITH TIME ZONE NULL;

ALTER TABLE "InstitutionStudents" DROP CONSTRAINT IF EXISTS "CK_InstitutionStudents_ConfirmationStatus";
ALTER TABLE "InstitutionStudents" ADD CONSTRAINT "CK_InstitutionStudents_ConfirmationStatus"
    CHECK ("ConfirmationStatus" IN ('pending', 'accepted', 'refused'));

CREATE INDEX IF NOT EXISTS "IX_InstitutionStudents_StudentId_ConfirmationStatus"
    ON "InstitutionStudents"("StudentId", "ConfirmationStatus");

-- ── Changements de titulaire du portefeuille institution ───────────────────
CREATE TABLE IF NOT EXISTS "InstitutionWalletHolderChanges" (
    "Id"               SERIAL PRIMARY KEY,
    "InstitutionId"    INTEGER NOT NULL,
    "OldHolderUserId"  INTEGER NULL,
    "NewHolderUserId"  INTEGER NOT NULL,
    "AmountXaf"        NUMERIC(14,0) NOT NULL DEFAULT 0,
    "ChangedByUserId"  INTEGER NOT NULL,
    "Reason"           VARCHAR(300) NULL,
    "ClientRequestId"  VARCHAR(64) NULL,
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_InstitutionWalletHolderChanges_Institutions_InstitutionId"
        FOREIGN KEY ("InstitutionId") REFERENCES "Institutions"("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_InstitutionWalletHolderChanges_AmountXaf" CHECK ("AmountXaf" >= 0)
);

CREATE INDEX IF NOT EXISTS "IX_InstitutionWalletHolderChanges_InstitutionId"
    ON "InstitutionWalletHolderChanges"("InstitutionId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_InstitutionWalletHolderChanges_InstitutionId_ClientRequestId"
    ON "InstitutionWalletHolderChanges"("InstitutionId", "ClientRequestId");

-- ── Correctif : TutorBookings.Status trop court ────────────────────────────
-- SQL_AddTutorBookingModule.sql l'a créé en VARCHAR(20), alors que le statut
-- « pending_tutor_approval » (paiement reçu, en attente du tuteur) compte 22
-- caractères : toute réservation payée par solde, et toute confirmation de
-- paiement NotchPay d'une réservation, échouait à l'enregistrement (erreur
-- PostgreSQL 22001). Élargissement sans perte, rejouable.
ALTER TABLE "TutorBookings" ALTER COLUMN "Status" TYPE VARCHAR(30);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012180000_InstitutionWalletHolderAndAttachmentConsent', '8.0.0')
ON CONFLICT DO NOTHING;
