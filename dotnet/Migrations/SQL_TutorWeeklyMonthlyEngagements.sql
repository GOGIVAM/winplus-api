-- Migration: TutorWeeklyMonthlyEngagements
-- Lot 9.4 (suivi Partie 17.7, décisions du 2026-10-11 précisées le 2026-10-12) :
--   engagement mensuel à créneaux fixes (mêmes créneaux chaque semaine pendant
--   4 semaines), séances générées et bloquées, paiement d'avance, argent libéré
--   séance par séance, renouvellement manuel avec option automatique.
-- Equivalent EF : Models/Entities/TutorEngagement.cs (TutorEngagement,
-- TutorEngagementSlot), TutorProfile.cs (TutorPackage.Kind / DiscountPercent),
-- TutorBooking.cs (TutorEngagementId, ObjectType monthly_session).
--
-- Nom choisi pour passer après SQL_TutorPayersParticipantsGroupsAndAssignments.sql
-- et SQL_TutorWalletPaymentRefundsAndPolicy.sql dans l'ordre alphabétique ; le
-- script ne dépend de toute façon d'aucun des deux (il ne touche que des
-- colonnes et tables qu'il crée, plus TutorBookings.Status qu'il élargit si besoin).
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée
-- (les forfaits historiques deviennent Kind = 'bundle', inertes comme avant).

-- ── TutorPackages : l'offre mensuelle du tuteur ─────────────────────────────
ALTER TABLE "TutorPackages" ADD COLUMN IF NOT EXISTS "Kind"            VARCHAR(20) NOT NULL DEFAULT 'bundle';
ALTER TABLE "TutorPackages" ADD COLUMN IF NOT EXISTS "DiscountPercent" INTEGER     NOT NULL DEFAULT 0;
ALTER TABLE "TutorPackages" ADD COLUMN IF NOT EXISTS "UpdatedAt"       TIMESTAMP WITH TIME ZONE NULL;

ALTER TABLE "TutorPackages" DROP CONSTRAINT IF EXISTS "CK_TutorPackages_DiscountPercent";
ALTER TABLE "TutorPackages" ADD CONSTRAINT "CK_TutorPackages_DiscountPercent"
    CHECK ("DiscountPercent" BETWEEN 0 AND 30);

-- Une seule offre mensuelle par profil de tuteur.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_TutorPackages_Monthly_TutorProfileId"
    ON "TutorPackages"("TutorProfileId") WHERE "Kind" = 'monthly';

-- ── TutorEngagements : l'achat du payeur ───────────────────────────────────
CREATE TABLE IF NOT EXISTS "TutorEngagements" (
    "Id"                       SERIAL PRIMARY KEY,
    "TutorProfileId"           INTEGER       NOT NULL,
    "TutorPackageId"           INTEGER       NULL,
    "PayerType"                VARCHAR(20)   NOT NULL DEFAULT 'student',
    "PayerUserId"              INTEGER       NOT NULL,
    "BookedByUserId"           INTEGER       NOT NULL,
    "PayerInstitutionId"       INTEGER       NULL,
    "StudyGroupId"             INTEGER       NULL,
    "TutorAssignmentId"        INTEGER       NULL,
    "ParticipantCount"         INTEGER       NOT NULL DEFAULT 1,
    "StudentUserId"            INTEGER       NOT NULL,
    "Subject"                  VARCHAR(100)  NULL,
    "Mode"                     VARCHAR(30)   NOT NULL DEFAULT 'online',
    "TopicsJson"               TEXT          NULL,
    "StartDate"                DATE          NOT NULL,
    "EndDate"                  DATE          NOT NULL,
    "Weeks"                    INTEGER       NOT NULL DEFAULT 4,
    "DiscountPercent"          INTEGER       NOT NULL DEFAULT 0,
    "SubtotalXaf"              NUMERIC(14,2) NOT NULL,
    "DiscountXaf"              NUMERIC(14,2) NOT NULL,
    "TotalXaf"                 NUMERIC(14,2) NOT NULL,
    "PaymentMethod"            VARCHAR(20)   NOT NULL,
    "PaymentStatus"            VARCHAR(20)   NOT NULL DEFAULT 'pending',
    "NotchpayReference"        VARCHAR(100)  NULL,
    "PhoneNumber"              VARCHAR(30)   NULL,
    "PaymentFeeXaf"            NUMERIC(14,2) NULL,
    -- 30 caractères : « pending_tutor_approval » en compte 22 (voir suivi 17.14).
    "Status"                   VARCHAR(30)   NOT NULL DEFAULT 'pending_payment',
    "ResponseDeadlineAt"       TIMESTAMP WITH TIME ZONE NULL,
    "RespondedAt"              TIMESTAMP WITH TIME ZONE NULL,
    "AutoRenew"                BOOLEAN       NOT NULL DEFAULT FALSE,
    "RenewedFromEngagementId"  INTEGER       NULL,
    "RenewedToEngagementId"    INTEGER       NULL,
    "RenewalReminderSentAt"    TIMESTAMP WITH TIME ZONE NULL,
    "AutoRenewAttemptedAt"     TIMESTAMP WITH TIME ZONE NULL,
    "AutoRenewFailureReason"   VARCHAR(300)  NULL,
    "CancellationReason"       VARCHAR(300)  NULL,
    "CancelledByUserId"        INTEGER       NULL,
    "CancelledAt"              TIMESTAMP WITH TIME ZONE NULL,
    "CreatedAt"                TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"                TIMESTAMP WITH TIME ZONE NULL,
    CONSTRAINT "FK_TutorEngagements_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorEngagements_TutorProfileId_Status" ON "TutorEngagements"("TutorProfileId", "Status");
CREATE INDEX IF NOT EXISTS "IX_TutorEngagements_PayerUserId"           ON "TutorEngagements"("PayerUserId");
CREATE INDEX IF NOT EXISTS "IX_TutorEngagements_BookedByUserId"        ON "TutorEngagements"("BookedByUserId");
CREATE INDEX IF NOT EXISTS "IX_TutorEngagements_PayerInstitutionId"    ON "TutorEngagements"("PayerInstitutionId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorEngagements_NotchpayReference" ON "TutorEngagements"("NotchpayReference");

-- ── TutorEngagementSlots : créneaux hebdomadaires de l'engagement ───────────
CREATE TABLE IF NOT EXISTS "TutorEngagementSlots" (
    "Id"                 SERIAL PRIMARY KEY,
    "TutorEngagementId"  INTEGER  NOT NULL,
    "DayOfWeek"          INTEGER  NOT NULL,
    "StartTime"          INTERVAL NOT NULL,
    "DurationMinutes"    INTEGER  NOT NULL,
    CONSTRAINT "FK_TutorEngagementSlots_TutorEngagements_TutorEngagementId"
        FOREIGN KEY ("TutorEngagementId") REFERENCES "TutorEngagements"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorEngagementSlots_TutorEngagementId" ON "TutorEngagementSlots"("TutorEngagementId");

-- ── TutorBookings : séances générées par un engagement ──────────────────────
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "TutorEngagementId" INTEGER NULL;
CREATE INDEX IF NOT EXISTS "IX_TutorBookings_TutorEngagementId" ON "TutorBookings"("TutorEngagementId");

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_TutorBookings_TutorEngagements_TutorEngagementId') THEN
        ALTER TABLE "TutorBookings" ADD CONSTRAINT "FK_TutorBookings_TutorEngagements_TutorEngagementId"
            FOREIGN KEY ("TutorEngagementId") REFERENCES "TutorEngagements"("Id") ON DELETE RESTRICT;
    END IF;
END $$;

-- Statuts des séances : « pending_tutor_approval » (22 caractères) doit tenir.
-- Déjà élargi à 30 par SQL_InstitutionWalletHolderAndAttachmentConsent.sql ;
-- répété ici (sans effet si déjà fait) pour ne dépendre d'aucun ordre.
DO $$
BEGIN
    IF (SELECT character_maximum_length FROM information_schema.columns
         WHERE table_name = 'TutorBookings' AND column_name = 'Status') < 30 THEN
        ALTER TABLE "TutorBookings" ALTER COLUMN "Status" TYPE VARCHAR(30);
    END IF;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012120000_TutorWeeklyMonthlyEngagements', '8.0.0')
ON CONFLICT DO NOTHING;
