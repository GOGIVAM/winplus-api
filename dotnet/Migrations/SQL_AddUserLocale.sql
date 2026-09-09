-- Migration: AddUserLocale
-- Ajoute la langue préférée de l'utilisateur ("fr" | "en"), pilotant l'UI web,
-- l'UI mobile et le choix de template pour les emails transactionnels
-- (EmailService). Défaut "fr" pour préserver le comportement actuel de tous
-- les comptes existants.
-- Equivalent EF entity: Models/Entities/User.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Locale" VARCHAR(5) NOT NULL DEFAULT 'fr';

-- Historique EF : évite que `dotnet ef migrations` s'y perde si l'équipe
-- exécute un jour les migrations normalement (le reste du projet applique
-- déjà ses évolutions par SQL manuel, voir les autres fichiers SQL_*.sql).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260909120000_AddUserLocale', '8.0.0')
ON CONFLICT DO NOTHING;
