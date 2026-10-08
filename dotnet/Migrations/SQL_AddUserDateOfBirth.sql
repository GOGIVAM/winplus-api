-- Migration: AddUserDateOfBirth
-- Décision 15.3 (arbitrages lot 7  administration et conformité, 2026-10-08) :
-- ajoute un champ de date de naissance, nullable et additif, pour fiabiliser
-- la vérification de minorité de l'export RGPD (module 43,
-- GET /api/users/me/export?childId=). En son absence sur un compte, le
-- comportement précédent reste inchangé (export autorisé, minorityVerified
-- renvoyé à false, jamais de blocage sur ce seul motif).
-- Equivalent EF entity: Models/Entities/User.cs (DateOfBirth)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Additif uniquement : aucune colonne retirée ni renommée.

ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "DateOfBirth" DATE NULL;

-- Historique EF : évite que `dotnet ef migrations` s'y perde si l'équipe
-- exécute un jour les migrations normalement (le reste du projet applique
-- déjà ses évolutions par SQL manuel, voir les autres fichiers SQL_*.sql).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261008120000_AddUserDateOfBirth', '8.0.0')
ON CONFLICT DO NOTHING;
