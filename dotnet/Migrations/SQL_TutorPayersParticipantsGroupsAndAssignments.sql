-- Migration: TutorPayersParticipantsGroupsAndAssignments
-- Lot 9.3 (suivi Partie 17, décisions du 2026-10-11) :
--   - 17.4 séparation payeur / participants / objet d'une réservation ;
--          réservation par un parent, une institution ou un groupe d'étude ;
--          attribution d'un tuteur (parent → enfant, institution → classe) ;
--          portefeuille institution (registre WalletTransactions existant).
--   - 17.3 séances de groupe : prix par élève dégressif ou prix fixe, taille max.
-- Equivalent EF : Models/Entities/TutorBooking.cs (TutorBookingParticipant),
-- TutorAssignment.cs, TutorPricing.cs (TutorGroupDiscount), TutorProfile.cs,
-- Institution.cs, WalletTransaction.cs (WalletOwnerTypes.Institution).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée. Les réservations existantes
-- (un élève qui paie) reçoivent un participant (l'élève) et un payeur
-- (l'élève) : exactement ce que le code supposait déjà quand ces valeurs
-- étaient NULL, leur comportement ne change pas.

-- ── TutorBookings : payeur, réservant, groupe, objet ───────────────────────
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "PayerType"          VARCHAR(20) NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "BookedByUserId"     INTEGER     NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "PayerInstitutionId" INTEGER     NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "StudyGroupId"       INTEGER     NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "ParticipantCount"   INTEGER     NOT NULL DEFAULT 1;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "TutorAssignmentId"  INTEGER     NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "ObjectType"         VARCHAR(20) NOT NULL DEFAULT 'single_session';

CREATE INDEX IF NOT EXISTS "IX_TutorBookings_PayerUserId"        ON "TutorBookings"("PayerUserId");
CREATE INDEX IF NOT EXISTS "IX_TutorBookings_PayerInstitutionId" ON "TutorBookings"("PayerInstitutionId");

-- Réservations existantes : payeur = l'élève (ne touche que les lignes encore NULL).
UPDATE "TutorBookings" SET "PayerType" = 'student' WHERE "PayerType" IS NULL;
UPDATE "TutorBookings" SET "PayerUserId" = "StudentUserId" WHERE "PayerUserId" IS NULL;
UPDATE "TutorBookings" SET "BookedByUserId" = "StudentUserId" WHERE "BookedByUserId" IS NULL;

-- ── TutorBookingParticipants : élèves qui suivent la séance ────────────────
CREATE TABLE IF NOT EXISTS "TutorBookingParticipants" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorBookingId"  INTEGER NOT NULL,
    "StudentUserId"   INTEGER NOT NULL,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_TutorBookingParticipants_TutorBookings_TutorBookingId"
        FOREIGN KEY ("TutorBookingId") REFERENCES "TutorBookings"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorBookingParticipants_Users_StudentUserId"
        FOREIGN KEY ("StudentUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorBookingParticipants_TutorBookingId_StudentUserId"
    ON "TutorBookingParticipants"("TutorBookingId", "StudentUserId");
CREATE INDEX IF NOT EXISTS "IX_TutorBookingParticipants_StudentUserId"
    ON "TutorBookingParticipants"("StudentUserId");

-- Un participant (l'élève) par réservation existante ; rejouable sans doublon.
INSERT INTO "TutorBookingParticipants" ("TutorBookingId", "StudentUserId", "CreatedAt")
SELECT b."Id", b."StudentUserId", b."CreatedAt"
  FROM "TutorBookings" b
 WHERE NOT EXISTS (SELECT 1 FROM "TutorBookingParticipants" p WHERE p."TutorBookingId" = b."Id");

-- ── TutorProfiles : méthode de prix de groupe (17.3) ───────────────────────
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "GroupPricingMethod"    VARCHAR(20) NULL;
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "MaxGroupSize"          INTEGER     NOT NULL DEFAULT 1;
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "GroupFlatPricePercent" INTEGER     NULL;

CREATE TABLE IF NOT EXISTS "TutorGroupDiscounts" (
    "Id"               SERIAL PRIMARY KEY,
    "TutorProfileId"   INTEGER NOT NULL,
    "MinGroupSize"     INTEGER NOT NULL,
    "PercentDiscount"  INTEGER NOT NULL,
    CONSTRAINT "FK_TutorGroupDiscounts_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorGroupDiscounts_TutorProfileId" ON "TutorGroupDiscounts"("TutorProfileId");

-- ── TutorAssignments : attribution d'un tuteur (relation suivie) ───────────
CREATE TABLE IF NOT EXISTS "TutorAssignments" (
    "Id"               SERIAL PRIMARY KEY,
    "TutorProfileId"   INTEGER NOT NULL,
    "AssignedByUserId" INTEGER NOT NULL,
    "AssignerType"     VARCHAR(20)  NOT NULL,
    "InstitutionId"    INTEGER      NULL,
    "GroupName"        VARCHAR(100) NULL,
    "Subject"          VARCHAR(100) NULL,
    "Note"             VARCHAR(500) NULL,
    "Status"           VARCHAR(20)  NOT NULL DEFAULT 'pending',
    "ResponseNote"     VARCHAR(300) NULL,
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "RespondedAt"      TIMESTAMP WITH TIME ZONE NULL,
    "EndedAt"          TIMESTAMP WITH TIME ZONE NULL,
    "EndedByUserId"    INTEGER      NULL,
    CONSTRAINT "FK_TutorAssignments_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorAssignments_Users_AssignedByUserId"
        FOREIGN KEY ("AssignedByUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS "IX_TutorAssignments_TutorProfileId_Status" ON "TutorAssignments"("TutorProfileId", "Status");
CREATE INDEX IF NOT EXISTS "IX_TutorAssignments_AssignedByUserId"      ON "TutorAssignments"("AssignedByUserId");
CREATE INDEX IF NOT EXISTS "IX_TutorAssignments_InstitutionId"         ON "TutorAssignments"("InstitutionId");

CREATE TABLE IF NOT EXISTS "TutorAssignmentBeneficiaries" (
    "Id"                 SERIAL PRIMARY KEY,
    "TutorAssignmentId"  INTEGER NOT NULL,
    "StudentUserId"      INTEGER NOT NULL,
    CONSTRAINT "FK_TutorAssignmentBeneficiaries_TutorAssignments_TutorAssignmentId"
        FOREIGN KEY ("TutorAssignmentId") REFERENCES "TutorAssignments"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorAssignmentBeneficiaries_Users_StudentUserId"
        FOREIGN KEY ("StudentUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorAssignmentBeneficiaries_TutorAssignmentId_StudentUserId"
    ON "TutorAssignmentBeneficiaries"("TutorAssignmentId", "StudentUserId");
CREATE INDEX IF NOT EXISTS "IX_TutorAssignmentBeneficiaries_StudentUserId"
    ON "TutorAssignmentBeneficiaries"("StudentUserId");

-- ── Portefeuille institution (17.4) ────────────────────────────────────────
-- Titulaire du portefeuille partagé de l'institution, fixé au premier usage.
ALTER TABLE "Institutions" ADD COLUMN IF NOT EXISTS "WalletOwnerUserId" INTEGER NULL;

-- Nouveau type de propriétaire 'institution' dans le registre (SQL_AddWalletLedger.sql
-- n'autorisait que teacher, parent, user, platform). Recréée à l'identique plus
-- 'institution' : rejouable, n'invalide aucune ligne existante.
ALTER TABLE "WalletTransactions" DROP CONSTRAINT IF EXISTS "CK_WalletTransactions_OwnerType";
ALTER TABLE "WalletTransactions" ADD CONSTRAINT "CK_WalletTransactions_OwnerType"
    CHECK ("OwnerType" IN ('teacher', 'parent', 'user', 'platform', 'institution'));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012090000_TutorPayersParticipantsGroupsAndAssignments', '8.0.0')
ON CONFLICT DO NOTHING;
