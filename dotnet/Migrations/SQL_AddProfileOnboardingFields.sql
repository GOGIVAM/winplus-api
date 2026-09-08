-- Migration: AddProfileOnboardingFields
-- Complète la table "Users" pour que /users/profile (CompleteProfile web +
-- complete_profile_screen mobile) persiste réellement les réponses de
-- l'onboarding au lieu de les envoyer dans le vide :
--   - Élève : filière/série et examen/concours visé (Specialization, TargetExam)
--   - Professeur : matières et niveaux enseignés (US-PRO-02), indépendant du
--     mode Répétiteur (TutorProfile.Subjects/Levels, voir SQL_AddTutorProfileModule.sql)
-- Equivalent EF entity: Models/Entities/User.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Specialization" VARCHAR(150);
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "TargetExam" VARCHAR(150);
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "TeachingSubjects" TEXT[] NOT NULL DEFAULT '{}';
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "TeachingLevels" TEXT[] NOT NULL DEFAULT '{}';

-- Historique EF : évite que `dotnet ef migrations` s'y perde si l'équipe
-- exécute un jour les migrations normalement (le reste du projet applique
-- déjà ses évolutions par SQL manuel, voir les autres fichiers SQL_*.sql).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260908220000_AddProfileOnboardingFields', '8.0.0')
ON CONFLICT DO NOTHING;
