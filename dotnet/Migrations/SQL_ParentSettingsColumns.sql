-- Migration: ParentSettingsColumns
-- Réglages du parent sortis de Users."Bio" (26/11/2026). PUT /api/parent/settings les rangeait en JSON dans la
-- bio ({"weeklyReport": true, "childrenComparison": false}), et le service du rapport hebdomadaire les y relisait :
-- un parent qui écrivait une vraie bio perdait ses réglages, et sa bio affichait du JSON.
-- Equivalent EF : Models/Entities/User.cs (ParentWeeklyReport, ParentChildrenComparison).
--
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Reprise : valeurs lues dans la bio JSON des parents, puis bio JSON vidée (une vraie bio n'est pas touchée).
-- À exécuter AVANT le déploiement du code.

ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ParentWeeklyReport" BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ParentChildrenComparison" BOOLEAN NULL;

DO $$
DECLARE
    r RECORD;
    j JSONB;
BEGIN
    FOR r IN SELECT "Id", "Bio" FROM "Users" WHERE "Bio" ~ '^\s*\{' LOOP
        BEGIN
            j := r."Bio"::jsonb;
        EXCEPTION WHEN others THEN
            CONTINUE; -- bio commençant par « { » mais pas du JSON : laissée telle quelle
        END;
        UPDATE "Users"
           SET "ParentWeeklyReport" = COALESCE((j ->> 'weeklyReport')::boolean, "ParentWeeklyReport"),
               "ParentChildrenComparison" = COALESCE((j ->> 'childrenComparison')::boolean, "ParentChildrenComparison"),
               "Bio" = NULL
         WHERE "Id" = r."Id"
           AND (j ? 'weeklyReport' OR j ? 'childrenComparison' OR j = '{}'::jsonb);
    END LOOP;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126220000_ParentSettingsColumns', '8.0.0')
ON CONFLICT DO NOTHING;
