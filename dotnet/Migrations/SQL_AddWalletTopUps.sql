-- Migration: AddWalletTopUps
-- Module 3 (lot 2) : recharge personnelle du portefeuille (décision §12 du suivi).
-- Idempotent. Ordre : après SQL_AddWalletLedger.sql, avant le code.
--
-- L'intention de recharge est une donnée structurée (cette table), liée à la
-- commande porteuse du paiement NotchPay et relue à la confirmation du
-- paiement : jamais un texte libre dans "Orders"."Notes".

CREATE TABLE IF NOT EXISTS "WalletTopUps" (
    "Id"              SERIAL PRIMARY KEY,
    "UserId"          INTEGER NOT NULL,
    "OrderId"         INTEGER NOT NULL,
    "AmountXaf"       NUMERIC(14,0) NOT NULL CHECK ("AmountXaf" > 0),
    "Status"          VARCHAR(20) NOT NULL DEFAULT 'pending',
    "ClientRequestId" VARCHAR(64),
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CompletedAt"     TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_WalletTopUps_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WalletTopUps_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTopUps_OrderId" ON "WalletTopUps" ("OrderId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTopUps_UserId_ClientRequestId"
    ON "WalletTopUps" ("UserId", "ClientRequestId") WHERE "ClientRequestId" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261003120000_AddWalletTopUps', '8.0.0')
ON CONFLICT DO NOTHING;
