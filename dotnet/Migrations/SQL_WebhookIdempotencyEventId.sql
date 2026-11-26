-- Migration: WebhookIdempotencyEventId
-- Aligne la table WebhookIdempotencyKeys sur le modèle du code (26/11/2026).
-- La table en base date d'une ancienne version : colonnes "Id", "Reference" (unique), "ProcessedAt".
-- Le code (PaymentService.HandleNotchPayWebhookAsync, via PaymentRepository) lit et écrit
-- "EventId", "Provider", "EventType" : chaque notification NotchPay d'une commande catalogue (WP-…)
-- levait « column w.EventId does not exist » (500), constaté sur l'intégration le 26/11 avec un
-- paiement simulé. La commande était ensuite confirmée par un autre chemin, mais l'idempotence
-- des notifications ne fonctionnait pas.
-- Equivalent EF : Models/Entities/WebhookIdempotencyKey.cs, ApplicationDbContext (index unique EventId + Provider).
--
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : "Reference" est gardée (rendue facultative), ses valeurs sont recopiées dans "EventId".

CREATE TABLE IF NOT EXISTS "WebhookIdempotencyKeys" (
    "Id"          SERIAL        PRIMARY KEY,
    "EventId"     VARCHAR(255)  NOT NULL,
    "Provider"    VARCHAR(50)   NOT NULL DEFAULT 'notchpay',
    "EventType"   VARCHAR(50)   NULL,
    "ProcessedAt" TIMESTAMPTZ   NOT NULL DEFAULT now()
);

ALTER TABLE "WebhookIdempotencyKeys" ADD COLUMN IF NOT EXISTS "EventId"   VARCHAR(255) NULL;
ALTER TABLE "WebhookIdempotencyKeys" ADD COLUMN IF NOT EXISTS "Provider"  VARCHAR(50)  NOT NULL DEFAULT 'notchpay';
ALTER TABLE "WebhookIdempotencyKeys" ADD COLUMN IF NOT EXISTS "EventType" VARCHAR(50)  NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'WebhookIdempotencyKeys' AND column_name = 'Reference') THEN
        -- Le code n'écrit plus "Reference" : elle ne doit plus bloquer les insertions.
        ALTER TABLE "WebhookIdempotencyKeys" ALTER COLUMN "Reference" DROP NOT NULL;
        UPDATE "WebhookIdempotencyKeys" SET "EventId" = "Reference" WHERE "EventId" IS NULL;
    END IF;
END $$;

UPDATE "WebhookIdempotencyKeys" SET "EventId" = 'legacy-' || "Id" WHERE "EventId" IS NULL;
ALTER TABLE "WebhookIdempotencyKeys" ALTER COLUMN "EventId" SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_WebhookIdempotencyKeys_EventId_Provider"
    ON "WebhookIdempotencyKeys" ("EventId", "Provider");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126190000_WebhookIdempotencyEventId', '8.0.0')
ON CONFLICT DO NOTHING;
