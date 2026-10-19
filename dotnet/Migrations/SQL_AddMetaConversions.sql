-- Migration: AddMetaConversions
-- Lot 10.3 bis (pixel Meta et API Conversions Meta) :
--   * Orders.Attribution_*, Users.FirstTouch_*, Users.LastTouch_* :
--     Fbclid (identifiant de clic d'une annonce Meta), Fbc et Fbp (cookies
--     du pixel), ClientIp et ClientUserAgent (relevés par le serveur), tous
--     gardés seulement avec le consentement publicité (colonne existante
--     *_ConsentAds) ;
--   * ConversionEvents : mêmes champs Meta, page d'arrivée (event_source_url)
--     et campagne utm (ventilation du résumé administrateur), avec reprise
--     de la campagne des conversions déjà enregistrées depuis leur commande.
-- Aucune donnée personnelle en clair : l'e-mail et le téléphone ne sont pas
-- copiés ici, ils sont hachés en SHA-256 au moment de l'envoi à Meta.
-- Equivalent EF : Models/Entities/MarketingAttribution.cs,
-- Data/ApplicationDbContext.Marketing.cs.
--
-- Prérequis : tables Users et Orders (schéma de base).
-- Indépendant de l'ordre : crée ConversionEvents s'il manque, avec la même
-- définition que SQL_AddMarketingAttribution.sql (dont le CREATE TABLE IF
-- NOT EXISTS ne fait alors plus rien), et pose les colonnes de
-- l'attribution qu'il utilise.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée.
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
        -- Colonnes du lot 10.3 lues par la reprise ci-dessous (déjà là si
        -- SQL_AddMarketingAttribution.sql est passé avant).
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmSource');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmMedium');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(150)', target.tbl, target.prefix || '_UtmCampaign');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(300)', target.tbl, target.prefix || '_LandingPage');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I BOOLEAN NULL', target.tbl, target.prefix || '_ConsentAds');
        -- Lot 10.3 bis.
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(255)', target.tbl, target.prefix || '_Fbclid');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(300)', target.tbl, target.prefix || '_Fbc');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(100)', target.tbl, target.prefix || '_Fbp');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(64)',  target.tbl, target.prefix || '_ClientIp');
        EXECUTE format('ALTER TABLE %I ADD COLUMN IF NOT EXISTS %I VARCHAR(500)', target.tbl, target.prefix || '_ClientUserAgent');
    END LOOP;
END $$;

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

ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "Fbc"             VARCHAR(300) NULL;
ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "Fbp"             VARCHAR(100) NULL;
ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "ClientIp"        VARCHAR(64)  NULL;
ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "ClientUserAgent" VARCHAR(500) NULL;
ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "LandingPage"     VARCHAR(300) NULL;
ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "UtmSource"       VARCHAR(150) NULL;
ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "UtmMedium"       VARCHAR(150) NULL;
ALTER TABLE "ConversionEvents" ADD COLUMN IF NOT EXISTS "UtmCampaign"     VARCHAR(150) NULL;

CREATE INDEX IF NOT EXISTS "IX_ConversionEvents_UtmSource_UtmCampaign"
    ON "ConversionEvents"("UtmSource", "UtmCampaign");

-- Reprise : campagne et page d'arrivée des conversions déjà enregistrées,
-- depuis l'attribution figée sur leur commande (rien pour le tutorat, qui
-- n'a pas de commande). Ne touche que les lignes encore vides.
UPDATE "ConversionEvents" c
SET "UtmSource"   = o."Attribution_UtmSource",
    "UtmMedium"   = o."Attribution_UtmMedium",
    "UtmCampaign" = o."Attribution_UtmCampaign",
    "LandingPage" = COALESCE(c."LandingPage", o."Attribution_LandingPage")
FROM "Orders" o
WHERE c."SourceType" = 'order'
  AND c."SourceId" = o."Id"
  AND c."UtmSource" IS NULL AND c."UtmCampaign" IS NULL
  AND (o."Attribution_UtmSource" IS NOT NULL OR o."Attribution_UtmCampaign" IS NOT NULL);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261019100000_AddMetaConversions', '8.0.0')
ON CONFLICT DO NOTHING;
