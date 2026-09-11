-- Migration: AddAffiliateProgram
-- Programme d'affiliation (profs/tuteurs). Décisions produit (2026-09-11) :
--   - Seuls les comptes "teacher" (couvre aussi le Mode Tuteur) peuvent devenir affiliés.
--   - La commission s'applique sur TOUT achat de la plateforme via le lien, pas
--     seulement sur le contenu de l'affilié.
--   - Le taux de commission est recalculé périodiquement par WinAI (avec repli
--     heuristique local si le service IA est indisponible), plafonné par
--     AffiliateSettings.CommissionRateCapPercent (réglable par l'admin).
--   - Fenêtre d'attribution : 30 jours après le clic.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "AffiliateAccounts" (
    "Id"               SERIAL PRIMARY KEY,
    "UserId"           INTEGER NOT NULL,
    "Code"             VARCHAR(32) NOT NULL,
    "CommissionRate"   NUMERIC(5,2) NOT NULL DEFAULT 0,
    "Status"           VARCHAR(20) NOT NULL DEFAULT 'active',
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastRateUpdateAt" TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_AffiliateAccounts_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AffiliateAccounts_UserId" ON "AffiliateAccounts"("UserId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AffiliateAccounts_Code" ON "AffiliateAccounts"("Code");

CREATE TABLE IF NOT EXISTS "AffiliateClicks" (
    "Id"                  SERIAL PRIMARY KEY,
    "AffiliateAccountId"  INTEGER NOT NULL,
    "VisitorToken"        VARCHAR(100) NOT NULL,
    "LandingPath"         VARCHAR(500),
    "ClickedAt"           TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_AffiliateClicks_AffiliateAccounts_AffiliateAccountId"
        FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_AffiliateClicks_Account_Visitor_ClickedAt"
    ON "AffiliateClicks"("AffiliateAccountId", "VisitorToken", "ClickedAt");

CREATE TABLE IF NOT EXISTS "AffiliateCommissions" (
    "Id"                     SERIAL PRIMARY KEY,
    "AffiliateAccountId"     INTEGER NOT NULL,
    "OrderId"                INTEGER NOT NULL,
    "BuyerUserId"            INTEGER,
    "OrderAmount"            NUMERIC(12,2) NOT NULL,
    "CommissionRateApplied"  NUMERIC(5,2) NOT NULL,
    "CommissionAmount"       NUMERIC(12,2) NOT NULL,
    "Status"                 VARCHAR(20) NOT NULL DEFAULT 'pending',
    "CreatedAt"              TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "ConfirmedAt"            TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_AffiliateCommissions_AffiliateAccounts_AffiliateAccountId"
        FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AffiliateCommissions_Orders_OrderId"
        FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AffiliateCommissions_OrderId" ON "AffiliateCommissions"("OrderId");
CREATE INDEX IF NOT EXISTS "IX_AffiliateCommissions_Account_Status" ON "AffiliateCommissions"("AffiliateAccountId", "Status");

CREATE TABLE IF NOT EXISTS "AffiliateSettings" (
    "Id"                        INTEGER PRIMARY KEY DEFAULT 1,
    "CommissionRateCapPercent"  NUMERIC(5,2) NOT NULL DEFAULT 15,
    "AttributionWindowDays"     INTEGER NOT NULL DEFAULT 30,
    "HoldPeriodDays"            INTEGER NOT NULL DEFAULT 14,
    "UpdatedAt"                 TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
INSERT INTO "AffiliateSettings" ("Id", "CommissionRateCapPercent", "AttributionWindowDays", "HoldPeriodDays")
VALUES (1, 15, 30, 14)
ON CONFLICT ("Id") DO NOTHING;

ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ReferralCode" VARCHAR(32);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260911130000_AddAffiliateProgram', '8.0.0')
ON CONFLICT DO NOTHING;
