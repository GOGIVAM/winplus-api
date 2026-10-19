-- Migration: AddUtmLinks
-- Lot 10.3 bis : historique du générateur de liens UTM de l'espace
-- administrateur (Marketing, Liens de campagne). Une ligne par lien créé :
-- adresse complète, chemin de destination, utm_source, utm_medium,
-- utm_campaign, utm_content facultatif, libellé libre, auteur et date.
-- Aucune donnée personnelle de visiteur.
-- Equivalent EF : Models/Entities/MarketingAttribution.cs (UtmLink),
-- Data/ApplicationDbContext.Marketing.cs.
--
-- Prérequis : aucun (pas de clé étrangère).
-- Indépendant de l'ordre, idempotent, additif.

CREATE TABLE IF NOT EXISTS "UtmLinks" (
    "Id"              SERIAL        PRIMARY KEY,
    "Url"             VARCHAR(1000) NOT NULL,
    "Destination"     VARCHAR(300)  NOT NULL DEFAULT '/',
    "UtmSource"       VARCHAR(150)  NOT NULL,
    "UtmMedium"       VARCHAR(150)  NOT NULL,
    "UtmCampaign"     VARCHAR(150)  NOT NULL,
    "UtmContent"      VARCHAR(150)  NULL,
    "Label"           VARCHAR(150)  NULL,
    "CreatedByUserId" INTEGER       NULL,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_UtmLinks_CreatedAt" ON "UtmLinks"("CreatedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261019100100_AddUtmLinks', '8.0.0')
ON CONFLICT DO NOTHING;
