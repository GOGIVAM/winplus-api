-- Migration: FixGoalsCompletedAtAndCertificateCodes
-- Deux écarts entre le modèle (EF) et la base, trouvés par le balayage des routes en lecture
-- du module de test (GET /student/stats, /student/goals, /certificates/..., /users/{id}/statistics
-- répondaient 500 pour tous les rôles) :
--
-- 1. Goal.CompletedAt (Models/Entities/Goal.cs) n'a jamais eu de colonne : "column g.CompletedAt does
--    not exist" à chaque lecture d'objectifs (statistiques élève, objectifs, progression).
-- 2. Certificates.VerificationCode : la propriété est déclarée obligatoire dans ApplicationDbContext
--    alors que des certificats existants ont un code NULL : "Column 'VerificationCode' is null" à
--    chaque lecture. On leur attribue un code, pour qu'ils restent vérifiables.
--
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Goals" ADD COLUMN IF NOT EXISTS "CompletedAt" TIMESTAMP WITH TIME ZONE NULL;

-- Objectifs déjà terminés : meilleure date connue (dernière mise à jour, sinon création).
UPDATE "Goals"
SET "CompletedAt" = COALESCE("UpdatedAt", "CreatedAt")
WHERE LOWER("Status") = 'completed' AND "CompletedAt" IS NULL;

-- Même format que CertificateService : 12 caractères hexadécimaux en majuscules.
UPDATE "Certificates"
SET "VerificationCode" = UPPER(SUBSTRING(MD5(RANDOM()::text || "Id"::text || CLOCK_TIMESTAMP()::text) FROM 1 FOR 12))
WHERE "VerificationCode" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261102000000_FixGoalsCompletedAtAndCertificateCodes', '8.0.0')
ON CONFLICT DO NOTHING;
