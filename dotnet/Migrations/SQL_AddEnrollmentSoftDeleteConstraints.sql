-- Migration: AddEnrollmentSoftDeleteConstraints
-- Module 21 (lot 3), décision §4.G du suivi : le certificat déjà obtenu
-- survit à la désinscription. La désinscription est devenue une suppression
-- logique côté code (EnrollmentService.UnenrollAsync) ; ce script aligne le
-- schéma déployé sur cette règle :
--   1. La relation Certificate -> Enrollment passe de CASCADE à SET NULL
--      (EnrollmentId devient nullable), pour qu'une suppression physique
--      résiduelle de la ligne d'inscription (ancien code, script externe...)
--      ne détruise plus jamais le certificat.
--   2. L'unicité (UserId, SubjectId) sur Enrollments devient partielle, aux
--      seules lignes actives ("IsDeleted" = false), pour qu'une réinscription
--      après désinscription logique ne se heurte plus à l'unicité.
--
-- Les colonnes IsDeleted/UnenrolledAt/UnenrollReason existent déjà depuis la
-- migration EF AddUnenrollToEnrollment (20260120) : non recréées ici.
--
-- Idempotent : peut être rejoué sans casser une base déjà migrée. Les noms de
-- contrainte/d'index réels ne sont pas supposés connus à l'avance (deux
-- mécanismes de versionnement de schéma coexistent dans ce projet, voir
-- SUIVI_VERSEMENTS_ET_CORRECTIONS.md §3.2 point 18) : ils sont recherchés
-- dynamiquement plutôt que codés en dur.
--
-- Ordre de déploiement : ce script AVANT le code .NET de ce lot (l'entité
-- Certificate.EnrollmentId y est déjà `int?`, ce qui échouerait au premier
-- INSERT/UPDATE si la colonne restait NOT NULL en base).

-- 1. Certificates.EnrollmentId nullable + FK en SET NULL.
DO $$
DECLARE fk_name text;
BEGIN
    SELECT tc.constraint_name INTO fk_name
    FROM information_schema.table_constraints tc
    JOIN information_schema.key_column_usage kcu
      ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
    WHERE tc.table_schema = 'public'
      AND tc.table_name = 'Certificates'
      AND tc.constraint_type = 'FOREIGN KEY'
      AND kcu.column_name = 'EnrollmentId'
    LIMIT 1;

    IF fk_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE "Certificates" DROP CONSTRAINT %I', fk_name);
    END IF;
END $$;

ALTER TABLE "Certificates" ALTER COLUMN "EnrollmentId" DROP NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE table_schema = 'public' AND table_name = 'Certificates'
          AND constraint_name = 'FK_Certificates_Enrollments_EnrollmentId'
    ) THEN
        ALTER TABLE "Certificates"
            ADD CONSTRAINT "FK_Certificates_Enrollments_EnrollmentId"
            FOREIGN KEY ("EnrollmentId") REFERENCES "Enrollments"("Id") ON DELETE SET NULL;
    END IF;
END $$;

-- 2. Unicité (UserId, SubjectId) sur Enrollments : remplacée par une version
--    partielle, limitée aux lignes actives.
DO $$
DECLARE idx_name text;
BEGIN
    SELECT indexname INTO idx_name
    FROM pg_indexes
    WHERE schemaname = 'public'
      AND tablename = 'Enrollments'
      AND indexdef ILIKE '%UNIQUE%'
      AND indexdef ILIKE '%"UserId"%'
      AND indexdef ILIKE '%"SubjectId"%'
      AND indexdef NOT ILIKE '%WHERE%' -- ne redétruit pas la version partielle si déjà en place
    LIMIT 1;

    IF idx_name IS NOT NULL THEN
        EXECUTE format('DROP INDEX %I', idx_name);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Enrollments_UserId_SubjectId_Active"
    ON "Enrollments" ("UserId", "SubjectId")
    WHERE "IsDeleted" = false;
