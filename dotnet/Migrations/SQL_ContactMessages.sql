-- Migration: ContactMessages
-- Formulaire de contact (25/11/2026) : messages des visiteurs et utilisateurs, gardés en base puis
-- envoyés par e-mail à l'équipe. Avant, les formulaires visaient des routes inexistantes (404).
-- Equivalent EF : Models/Entities/ContactMessage.cs, Data/ApplicationDbContext.ContactMessages.cs.
--
-- Prérequis : table Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "ContactMessages" (
    "Id"         SERIAL        PRIMARY KEY,
    "UserId"     INTEGER       NULL REFERENCES "Users"("Id") ON DELETE SET NULL,
    "Name"       VARCHAR(100)  NOT NULL,
    "Email"      VARCHAR(254)  NOT NULL,
    "Phone"      VARCHAR(30)   NULL,
    "Subject"    VARCHAR(150)  NOT NULL,
    "Message"    VARCHAR(5000) NOT NULL,
    "Source"     VARCHAR(20)   NOT NULL DEFAULT 'contact',
    "Locale"     VARCHAR(10)   NULL,
    "Status"     VARCHAR(20)   NOT NULL DEFAULT 'new',
    "EmailSent"  BOOLEAN       NOT NULL DEFAULT FALSE,
    "CreatedAt"  TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "HandledAt"  TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS "IX_ContactMessages_CreatedAt" ON "ContactMessages"("CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_ContactMessages_Status" ON "ContactMessages"("Status");
