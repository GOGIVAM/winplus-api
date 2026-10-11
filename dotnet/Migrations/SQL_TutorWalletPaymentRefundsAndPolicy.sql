-- Migration: TutorWalletPaymentRefundsAndPolicy
-- Lot 9.2 (suivi Partie 17, décisions du 2026-10-11) :
--   - 17.6  paiement d'une séance par le solde du portefeuille (en plus de Mobile Money)
--   - 17.9  remboursements automatiques au portefeuille du payeur, règle des frais NotchPay
--   - 17.10 limites plateforme des réglages d'annulation du tuteur
-- Equivalent EF : Models/Entities/TutorBooking.cs (colonnes ci-dessous),
-- Services/TutorPolicy.cs (bornes).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée. Les réservations existantes
-- gardent PaymentMethod/RefundedAt à NULL : celles remboursées avant le 9.2
-- (remboursement manuel déjà fait hors plateforme) ne sont jamais recréditées.
-- Aucune nouvelle table : les écritures de portefeuille vont dans
-- "WalletTransactions" (nouveaux EntryType TutoringPayment, TutoringRefund,
-- TutoringFeeCharge, PaymentFeeRecovered ; colonne libre, sans contrainte CHECK).

-- ── TutorBookings : moyen de paiement, payeur, frais, remboursement ───────
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "PaymentMethod"      VARCHAR(20)   NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "PayerUserId"        INTEGER       NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "PaymentFeeXaf"      NUMERIC(10,2) NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "RefundedAt"         TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "RefundCause"        VARCHAR(30)   NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "RefundAmountXaf"    NUMERIC(10,2) NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "RefundFeeXaf"       NUMERIC(10,2) NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "RefundFeeBorneBy"   VARCHAR(20)   NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "TutorFeeChargedXaf" NUMERIC(10,2) NULL;
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "TutorRetainedXaf"   NUMERIC(10,2) NULL;

-- Réservations déjà payées par NotchPay : moyen de paiement renseigné
-- (affichage), sans toucher aux frais (recalculés au taux courant si besoin).
UPDATE "TutorBookings"
   SET "PaymentMethod" = 'mobile_money'
 WHERE "PaymentMethod" IS NULL AND "NotchpayReference" IS NOT NULL;

-- ── TutorProfiles : réglages d'annulation ramenés dans les bornes ─────────
-- Bornes (Services/TutorPolicy.cs) : remboursement intégral entre 12 et 48 h
-- avant la séance, part partielle entre 0 et 100 %, aucun remboursement en
-- dessous de 0 à 12 h (et jamais au-dessus du seuil intégral). Chaque UPDATE
-- ne touche que les lignes hors bornes : rejouer le script ne change rien.
UPDATE "TutorProfiles"
   SET "FullRefundHours" = LEAST(GREATEST("FullRefundHours", 12), 48)
 WHERE "FullRefundHours" < 12 OR "FullRefundHours" > 48;

UPDATE "TutorProfiles"
   SET "PartialRefundPercent" = LEAST(GREATEST("PartialRefundPercent", 0), 100)
 WHERE "PartialRefundPercent" < 0 OR "PartialRefundPercent" > 100;

UPDATE "TutorProfiles"
   SET "NoRefundHours" = LEAST(GREATEST("NoRefundHours", 0), 12, "FullRefundHours")
 WHERE "NoRefundHours" < 0 OR "NoRefundHours" > 12 OR "NoRefundHours" > "FullRefundHours";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011180000_TutorWalletPaymentRefundsAndPolicy', '8.0.0')
ON CONFLICT DO NOTHING;
