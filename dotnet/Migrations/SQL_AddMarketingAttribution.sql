-- Migration: AddMarketingAttribution
-- Lot 10.3 (consentement et mesure, étude 10 section B) :
--   * Orders.Attribution_* : source d'acquisition au moment de la commande
--     (gclid, gbraid, wbraid, utm, référent, page d'arrivée, identifiant
--     client GA4) et consentement du visiteur à ce moment-là ;
--   * Users.FirstTouch_* et Users.LastTouch_* : première et dernière source
--     connues du compte, mêmes champs ;
--   * ConversionEvents : registre des achats confirmés (un par
--     transaction_id), source de l'envoi serveur GA4 et de l'export CSV des
--     conversions hors ligne Google Ads ;
--   * ConversionDeliveries : file d'envoi par destination (ga4 aujourd'hui),
--     avec reprise sur erreur et un seul envoi par conversion et destination.
-- Aucune donnée personnelle en clair (ni e-mail, ni nom, ni téléphone).
-- Equivalent EF : Models/Entities/MarketingAttribution.cs,
-- Data/ApplicationDbContext.Marketing.cs, Models/Entities/Order.cs, User.cs.
--
-- Prérequis : tables Users et Orders (schéma de base).
-- Indépendant de l'ordre : n'utilise que des tables de base, ne contredit
-- aucun autre script.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée modifiée.
-- À exécuter AVANT le déploiement du code.

-- ── Attribution sur la commande et sur le compte ─────────────────────────
DO $$
DECLARE
    target RECORD;
BEGIN
    FOR target IN
        SELECT * FROM (VALUES
            ('Orders', 'Attribution'),
            ('Users',  'FirstTouch'),
            ('Users',  'LastTouch')
        ) AS t(tbl, prefix)
    LOOP
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(255)', target.tbl, target.prefix || '_Gclid');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(255)', target.tbl, target.prefix || '_Gbraid');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(255)', target.tbl, target.prefix || '_Wbraid');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmSource');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmMedium');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmCampaign');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmTerm');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmContent');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(255)', target.tbl, target.prefix || '_Referrer');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(300)', target.tbl, target.prefix || '_LandingPage');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(100)', target.tbl, target.prefix || '_GaClientId');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(50)',  target.tbl, target.prefix || '_GaSessionId');
        -- Type possédé facultatif : colonnes nullables, NULL partout = pas d'attribution.
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I BOOLEAN NULL', target.tbl, target.prefix || '_ConsentAnalytics');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I BOOLEAN NULL', target.tbl, target.prefix || '_ConsentAds');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I TIMESTAMP WITH TIME ZONE NULL', target.tbl, target.prefix || '_CapturedAt');
    END LOOP;
END $$;

-- Export Google Ads : commandes portant un gclid.
CREATE INDEX IF NOT EXISTS "IX_Orders_Attribution_Gclid" ON "Orders"("Attribution_Gclid")
    WHERE "Attribution_Gclid" IS NOT NULL;

-- ── Registre des conversions ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "ConversionEvents" (
    "Id"               SERIAL        PRIMARY KEY,
    "TransactionId"    VARCHAR(100)  NOT NULL,
    "EventName"        VARCHAR(50)   NOT NULL DEFAULT 'purchase',
    "Category"         VARCHAR(30)   NOT NULL DEFAULT 'mixte',
    "SourceType"       VARCHAR(30)   NOT NULL DEFAULT 'order',
    "SourceId"         INTEGER       NOT NULL,
    "UserId"           INTEGER       NULL,
    "ValueXaf"         NUMERIC(12,2) NOT NULL DEFAULT 0,
    "Currency"         VARCHAR(3)    NOT NULL DEFAULT 'XAF',
    "OccurredAt"       TIMESTAMP WITH TIME ZONE NOT NULL,
    "Gclid"            VARCHAR(255)  NULL,
    "Gbraid"           VARCHAR(255)  NULL,
    "Wbraid"           VARCHAR(255)  NULL,
    "GaClientId"       VARCHAR(100)  NULL,
    "GaSessionId"      VARCHAR(50)   NULL,
    "ConsentAnalytics" BOOLEAN       NOT NULL DEFAULT FALSE,
    "ConsentAds"       BOOLEAN       NOT NULL DEFAULT FALSE,
    "ItemsJson"        TEXT          NULL,
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ConversionEvents_EventName_TransactionId"
    ON "ConversionEvents"("EventName", "TransactionId");
CREATE INDEX IF NOT EXISTS "IX_ConversionEvents_OccurredAt" ON "ConversionEvents"("OccurredAt");

-- ── File d'envoi par destination ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "ConversionDeliveries" (
    "Id"                SERIAL        PRIMARY KEY,
    "ConversionEventId" INTEGER       NOT NULL REFERENCES "ConversionEvents"("Id") ON DELETE CASCADE,
    "Destination"       VARCHAR(30)   NOT NULL,
    "Status"            VARCHAR(20)   NOT NULL DEFAULT 'pending',
    "Attempts"          INTEGER       NOT NULL DEFAULT 0,
    "NextAttemptAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastError"         VARCHAR(1000) NULL,
    "SentAt"            TIMESTAMP WITH TIME ZONE NULL,
    "CreatedAt"         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ConversionDeliveries_ConversionEventId_Destination"
    ON "ConversionDeliveries"("ConversionEventId", "Destination");
CREATE INDEX IF NOT EXISTS "IX_ConversionDeliveries_Status_NextAttemptAt"
    ON "ConversionDeliveries"("Status", "NextAttemptAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261018150000_AddMarketingAttribution', '8.0.0')
ON CONFLICT DO NOTHING;
