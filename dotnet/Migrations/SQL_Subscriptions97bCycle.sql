-- Migration: Subscriptions97bCycle
-- Lot 9.7b, étape 3 (étude sections 0.2 et 5, suivi 18.14, 18.15, 18.17, 18.20) :
--   * Subscriptions : cycle d'abonnement (montant payé, commande, période en
--     cours, renouvellement automatique, résiliation en fin de période, descente
--     programmée, remise d'un code sur N périodes, essai) ;
--   * un seul abonnement en vigueur par compte (index unique partiel) ;
--   * PromoCodes : modèle de promotion unifié (nature, offre automatique, cibles
--     multiples, plans, publics, périodes, durée de la remise, essai, tokens
--     offerts, rôles, éligibilité) ; PromoCodeUsages : usage sans commande
--     (essai) rattaché à un abonnement ;
--   * AiTokenUsages.FromTopUp et AiTokenCredits : recharge de tokens WinAI ;
--   * suppression de la table Promotions (entité morte, table vide).
-- Equivalent EF : Models/Entities/Subscription.cs, PromoCode.cs, AiTokenUsage.cs,
-- AiTokenCredit.cs, Data/ApplicationDbContext.Subscriptions97b.cs.
--
-- Aucun prérequis d'ordre : ne dépend que de tables historiques. Idempotent.
-- Données : les codes promo existants reçoivent la nature correspondant à leur
-- type historique (FixedAmount → discount_amount) ; aucune autre donnée n'est
-- modifiée. L'index « un abonnement en vigueur par compte » n'est créé que si
-- les données le permettent ; sinon un avis est émis et le script manuel de
-- migration (scripts/9.7b/migrate_subscriptions.sql) le crée après nettoyage.
-- À exécuter AVANT le déploiement du code.
-- Longueurs : Kind VARCHAR(20) (« discount_percent » = 16), SubscriptionDuration
-- VARCHAR(20) (« first_payment » = 13), Eligibility VARCHAR(30)
-- (« lapsed_subscribers » = 18), AppliesTo VARCHAR(120) (5 cibles = 66),
-- Target VARCHAR(30) (« institution_license » = 19), Origin VARCHAR(20),
-- Subscriptions.Status (« past_due », « trialing » = 8).

-- ── Abonnements ──────────────────────────────────────────────────────────
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "PricePaidTtc"               NUMERIC(12,0) NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "OrderId"                    INTEGER       NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "CurrentPeriodStart"         TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "AutoRenew"                  BOOLEAN       NOT NULL DEFAULT FALSE;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "CancelAtPeriodEnd"          BOOLEAN       NOT NULL DEFAULT FALSE;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "CancelledAt"                TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "PendingPricingPlanId"       INTEGER       NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "PendingBillingPeriod"       VARCHAR(20)   NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "PromoCodeId"                INTEGER       NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "RemainingDiscountedPeriods" INTEGER       NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "TrialEndsAt"                TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "LastRenewalAttemptAt"       TIMESTAMP WITH TIME ZONE NULL;
CREATE INDEX IF NOT EXISTS "IX_Subscriptions_OrderId" ON "Subscriptions"("OrderId");
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_Subscriptions_Status') THEN
        ALTER TABLE "Subscriptions" ADD CONSTRAINT "CK_Subscriptions_Status"
            CHECK (LOWER("Status") IN ('active', 'trialing', 'past_due', 'cancelled', 'expired'));
    END IF;
EXCEPTION WHEN check_violation THEN
    RAISE NOTICE 'CK_Subscriptions_Status non posée : statuts hérités inattendus, à nettoyer par le script de migration.';
END $$;

-- Un seul abonnement en vigueur par compte (18.15).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'UX_Subscriptions_OneInForcePerUser') THEN
        IF EXISTS (
            SELECT "UserId" FROM "Subscriptions"
             WHERE "IsDeleted" = FALSE AND "IsActive" = TRUE AND LOWER("Status") IN ('active', 'trialing', 'past_due')
             GROUP BY "UserId" HAVING COUNT(*) > 1) THEN
            RAISE NOTICE 'UX_Subscriptions_OneInForcePerUser non créé : plusieurs abonnements en vigueur pour un même compte (voir scripts/9.7b/migrate_subscriptions.sql).';
        ELSE
            CREATE UNIQUE INDEX "UX_Subscriptions_OneInForcePerUser" ON "Subscriptions"("UserId")
                WHERE "IsDeleted" = FALSE AND "IsActive" = TRUE AND LOWER("Status") IN ('active', 'trialing', 'past_due');
        END IF;
    END IF;
END $$;

-- ── Promotion unifiée ────────────────────────────────────────────────────
ALTER TABLE "PromoCodes" DROP CONSTRAINT IF EXISTS "CK_PromoCodes_AppliesTo";
ALTER TABLE "PromoCodes" ALTER COLUMN "AppliesTo" TYPE VARCHAR(120);
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "Kind"                   VARCHAR(20) NOT NULL DEFAULT 'discount_percent';
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "RequiresCode"           BOOLEAN     NOT NULL DEFAULT TRUE;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "PlanIds"                TEXT        NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "Audiences"              VARCHAR(60) NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "BillingPeriods"         VARCHAR(60) NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "SubscriptionDuration"   VARCHAR(20) NOT NULL DEFAULT 'first_payment';
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "DurationPeriods"        INTEGER     NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "TrialDays"              INTEGER     NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "BonusTokens"            INTEGER     NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "TargetRoles"            VARCHAR(80) NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "Eligibility"            VARCHAR(30) NOT NULL DEFAULT 'everyone';
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "NewAccountDays"         INTEGER     NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "EligibleUserIds"        TEXT        NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "EligibleInstitutionIds" TEXT        NULL;
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "Stackable"              BOOLEAN     NOT NULL DEFAULT FALSE;
UPDATE "PromoCodes" SET "Kind" = 'discount_amount'
 WHERE "DiscountType" = 'FixedAmount' AND "Kind" = 'discount_percent';
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PromoCodes_Kind') THEN
        ALTER TABLE "PromoCodes" ADD CONSTRAINT "CK_PromoCodes_Kind"
            CHECK ("Kind" IN ('discount_percent', 'discount_amount', 'trial_days', 'bonus_tokens'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PromoCodes_SubscriptionDuration') THEN
        ALTER TABLE "PromoCodes" ADD CONSTRAINT "CK_PromoCodes_SubscriptionDuration"
            CHECK ("SubscriptionDuration" IN ('first_payment', 'all_periods', 'n_periods'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PromoCodes_Eligibility') THEN
        ALTER TABLE "PromoCodes" ADD CONSTRAINT "CK_PromoCodes_Eligibility"
            CHECK ("Eligibility" IN ('everyone', 'new_accounts', 'never_subscribed', 'lapsed_subscribers', 'listed'));
    END IF;
END $$;

ALTER TABLE "PromoCodeUsages" ALTER COLUMN "OrderId" DROP NOT NULL;
ALTER TABLE "PromoCodeUsages" ADD COLUMN IF NOT EXISTS "SubscriptionId" INTEGER     NULL;
ALTER TABLE "PromoCodeUsages" ADD COLUMN IF NOT EXISTS "Target"         VARCHAR(30) NULL;
CREATE INDEX IF NOT EXISTS "IX_PromoCodeUsages_SubscriptionId" ON "PromoCodeUsages"("SubscriptionId");

-- ── Recharge de tokens WinAI ─────────────────────────────────────────────
ALTER TABLE "AiTokenUsages" ADD COLUMN IF NOT EXISTS "FromTopUp" BOOLEAN NOT NULL DEFAULT FALSE;
CREATE TABLE IF NOT EXISTS "AiTokenCredits" (
    "Id"             SERIAL        PRIMARY KEY,
    "UserId"         INTEGER       NOT NULL,
    "Tokens"         BIGINT        NOT NULL,
    "PricePaidXaf"   NUMERIC(12,0) NOT NULL DEFAULT 0,
    "Origin"         VARCHAR(20)   NOT NULL DEFAULT 'topup',
    "PromoCodeId"    INTEGER       NULL,
    "IdempotencyKey" VARCHAR(80)   NOT NULL,
    "CreatedAt"      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_AiTokenCredits_UserId" ON "AiTokenCredits"("UserId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AiTokenCredits_IdempotencyKey" ON "AiTokenCredits"("IdempotencyKey");

-- ── Entité Promotion supprimée (table vide, aucun appelant) ──────────────
DO $$
BEGIN
    IF to_regclass('"Promotions"') IS NOT NULL THEN
        IF (SELECT COUNT(*) FROM "Promotions") = 0 THEN
            DROP TABLE "Promotions";
        ELSE
            ALTER TABLE "Promotions" RENAME TO "Promotions_archive_97b";
            RAISE NOTICE 'Table Promotions non vide : renommée en Promotions_archive_97b.';
        END IF;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261016100000_Subscriptions97bCycle', '8.0.0')
ON CONFLICT DO NOTHING;
