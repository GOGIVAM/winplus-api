-- Migration: ClientRequestKeys
-- Mode hors ligne du mobile (lot 3) : écritures déjà traitées par en-tête
-- Idempotency-Key. Une action rejouée par la file d'attente du téléphone,
-- dont la première réponse s'est perdue, reçoit la première réponse au lieu
-- d'être exécutée une seconde fois (Middlewares/ClientIdempotencyMiddleware.cs).
--
-- Prérequis : table Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif uniquement.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "ClientRequestKeys" (
    "Id"           SERIAL        PRIMARY KEY,
    "UserId"       INTEGER       NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Key"          VARCHAR(100)  NOT NULL,
    "Method"       VARCHAR(10)   NOT NULL,
    "Path"         VARCHAR(255)  NOT NULL,
    "StatusCode"   INTEGER       NOT NULL,
    "ResponseBody" TEXT          NULL,
    "CreatedAt"    TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ClientRequestKeys_UserId_Key"
    ON "ClientRequestKeys" ("UserId", "Key");
CREATE INDEX IF NOT EXISTS "IX_ClientRequestKeys_CreatedAt"
    ON "ClientRequestKeys" ("CreatedAt");
