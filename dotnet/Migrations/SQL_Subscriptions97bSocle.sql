-- Migration: Subscriptions97bSocle
-- Lot 9.7b, étape 1 (étude ETUDE_9_7B_ABONNEMENTS.md section 1.2, suivi 18.10,
-- 18.12, 18.13, 18.19, 18.21) : plans versionnés et droits typés.
--   * PricingPlans devient une famille stable : PlanKey, Audience, IsFreeTier, SortOrder ;
--   * PlanVersions : version d'un plan (brouillon, publiée, archivée), préavis 18.12 ;
--   * PlanPrices : prix TTC par période (monthly, quarterly, yearly) ;
--   * PlanEntitlements : valeur de chaque droit (clé typée) par version ;
--   * Subscriptions : version payée, période, public, fin de grâce.
-- Equivalent EF : Data/ApplicationDbContext.Subscriptions97b.cs,
-- Models/Entities/PlanCatalog.cs, PricingPlan.cs, Subscription.cs.
--
-- Aucun prérequis d'ordre : ne dépend que des tables historiques PricingPlans
-- et Subscriptions ; peut passer avant ou après n'importe quel autre script.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ; aucune donnée existante modifiée (la
-- grille initiale est posée par SQL_Subscriptions97bGrid.sql, la migration
-- des abonnements par le script manuel scripts/9.7b/migrate_subscriptions.sql).
-- À exécuter AVANT le déploiement du code (le modèle EF lit ces colonnes).
-- Longueurs : Status VARCHAR(20) (« published » = 9, « archived » = 8),
-- Period / BillingPeriod VARCHAR(20) (« quarterly » = 9), Audience VARCHAR(20)
-- (« teacher » = 7), Key VARCHAR(80) (« teacher.max_students_per_class » = 30),
-- Value VARCHAR(100), PlanKey VARCHAR(50).

-- ── Familles de plans ────────────────────────────────────────────────────
ALTER TABLE "PricingPlans" ADD COLUMN IF NOT EXISTS "PlanKey"    VARCHAR(50) NULL;
ALTER TABLE "PricingPlans" ADD COLUMN IF NOT EXISTS "Audience"   VARCHAR(20) NULL;
ALTER TABLE "PricingPlans" ADD COLUMN IF NOT EXISTS "IsFreeTier" BOOLEAN     NOT NULL DEFAULT FALSE;
ALTER TABLE "PricingPlans" ADD COLUMN IF NOT EXISTS "SortOrder"  INTEGER     NOT NULL DEFAULT 0;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PricingPlans_PlanKey" ON "PricingPlans"("PlanKey");
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PricingPlans_Audience') THEN
        ALTER TABLE "PricingPlans" ADD CONSTRAINT "CK_PricingPlans_Audience"
            CHECK ("Audience" IS NULL OR "Audience" IN ('student', 'parent', 'teacher'));
    END IF;
END $$;
-- Un seul plan gratuit actif par public : il fonde les droits sans abonnement.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PricingPlans_FreeTier_Audience"
    ON "PricingPlans"("Audience") WHERE "IsFreeTier" = TRUE AND "IsDeleted" = FALSE;

-- ── Versions ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "PlanVersions" (
    "Id"                      SERIAL        PRIMARY KEY,
    "PricingPlanId"           INTEGER       NOT NULL REFERENCES "PricingPlans"("Id") ON DELETE RESTRICT,
    "VersionNumber"           INTEGER       NOT NULL,
    "Status"                  VARCHAR(20)   NOT NULL DEFAULT 'draft',
    "Notes"                   VARCHAR(1000) NULL,
    "CreatedAt"               TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedByUserId"         INTEGER       NULL,
    "PublishedAt"             TIMESTAMP WITH TIME ZONE NULL,
    "PublishedByUserId"       INTEGER       NULL,
    "ExistingSubscribersFrom" TIMESTAMP WITH TIME ZONE NULL,
    "IsImprovement"           BOOLEAN       NOT NULL DEFAULT FALSE,
    "ArchivedAt"              TIMESTAMP WITH TIME ZONE NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlanVersions_PricingPlanId_VersionNumber" ON "PlanVersions"("PricingPlanId", "VersionNumber");
CREATE INDEX IF NOT EXISTS "IX_PlanVersions_PricingPlanId_Status" ON "PlanVersions"("PricingPlanId", "Status");
-- Une seule version publiée et un seul brouillon par famille.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PlanVersions_Published" ON "PlanVersions"("PricingPlanId") WHERE "Status" = 'published';
CREATE UNIQUE INDEX IF NOT EXISTS "UX_PlanVersions_Draft" ON "PlanVersions"("PricingPlanId") WHERE "Status" = 'draft';
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PlanVersions_Status') THEN
        ALTER TABLE "PlanVersions" ADD CONSTRAINT "CK_PlanVersions_Status"
            CHECK ("Status" IN ('draft', 'published', 'archived'));
    END IF;
END $$;

-- ── Prix TTC par période ─────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "PlanPrices" (
    "Id"            SERIAL         PRIMARY KEY,
    "PlanVersionId" INTEGER        NOT NULL REFERENCES "PlanVersions"("Id") ON DELETE CASCADE,
    "Period"        VARCHAR(20)    NOT NULL,
    "PriceTtc"      NUMERIC(12,0)  NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlanPrices_PlanVersionId_Period" ON "PlanPrices"("PlanVersionId", "Period");
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PlanPrices_Period') THEN
        ALTER TABLE "PlanPrices" ADD CONSTRAINT "CK_PlanPrices_Period"
            CHECK ("Period" IN ('monthly', 'quarterly', 'yearly') AND "PriceTtc" >= 0);
    END IF;
END $$;

-- ── Droits par version ───────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "PlanEntitlements" (
    "Id"            SERIAL        PRIMARY KEY,
    "PlanVersionId" INTEGER       NOT NULL REFERENCES "PlanVersions"("Id") ON DELETE CASCADE,
    "Key"           VARCHAR(80)   NOT NULL,
    "Value"         VARCHAR(100)  NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlanEntitlements_PlanVersionId_Key" ON "PlanEntitlements"("PlanVersionId", "Key");

-- ── Abonnements ──────────────────────────────────────────────────────────
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "PlanVersionId" INTEGER     NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "BillingPeriod" VARCHAR(20) NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "Audience"      VARCHAR(20) NULL;
ALTER TABLE "Subscriptions" ADD COLUMN IF NOT EXISTS "GraceUntil"    TIMESTAMP WITH TIME ZONE NULL;
-- Statut : « past_due » (8) et « trialing » (8) tiennent dans la colonne
-- historique ; on s'assure qu'elle n'est pas plus courte que 20.
DO $$
DECLARE len INTEGER;
BEGIN
    SELECT character_maximum_length INTO len FROM information_schema.columns
     WHERE table_name = 'Subscriptions' AND column_name = 'Status';
    IF len IS NOT NULL AND len < 20 THEN
        ALTER TABLE "Subscriptions" ALTER COLUMN "Status" TYPE VARCHAR(20);
    END IF;
END $$;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Subscriptions_PlanVersions_PlanVersionId') THEN
        ALTER TABLE "Subscriptions" ADD CONSTRAINT "FK_Subscriptions_PlanVersions_PlanVersionId"
            FOREIGN KEY ("PlanVersionId") REFERENCES "PlanVersions"("Id") ON DELETE RESTRICT;
    END IF;
END $$;
CREATE INDEX IF NOT EXISTS "IX_Subscriptions_PlanVersionId" ON "Subscriptions"("PlanVersionId");

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261015120000_Subscriptions97bSocle', '8.0.0')
ON CONFLICT DO NOTHING;
