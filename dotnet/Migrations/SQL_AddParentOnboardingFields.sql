-- Migration: AddParentOnboardingFields (Module 15, lot 6)
-- Le parent n'avait aucun onboarding : il était redirigé directement vers son
-- espace (voir commentaire historique dans CompleteProfile.tsx /
-- complete_profile_screen.dart). Ce script ajoute les deux champs collectés
-- par le nouvel onboarding parent : un objectif libre pour l'enfant/les
-- enfants, et une préférence de suivi ("close" | "weekly" | "minimal").
-- Equivalent EF entity: Models/Entities/User.cs (ParentChildObjective,
-- ParentFollowUpPreference).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ParentChildObjective" VARCHAR(300);
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ParentFollowUpPreference" VARCHAR(30);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261008090000_AddParentOnboardingFields', '8.0.0')
ON CONFLICT DO NOTHING;
