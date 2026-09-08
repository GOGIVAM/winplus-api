-- Migration: AddMessageTemplates
-- Module 7 — Messagerie (professeur_complete.md), US-MSG-08
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "MessageTemplates" (
    "Id"        SERIAL PRIMARY KEY,
    "UserId"    INTEGER NOT NULL,
    "Name"      VARCHAR(60) NOT NULL,
    "Text"      VARCHAR(1000) NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_MessageTemplates_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_MessageTemplates_UserId" ON "MessageTemplates"("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907203000_AddMessageTemplates', '8.0.0')
ON CONFLICT DO NOTHING;
