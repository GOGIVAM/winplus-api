-- Migration: WinAiSources
-- Atelier WinAI (25/11/2026) : documents personnels gardés comme sources de WinAI.
--   * WinAiSources : un document téléversé par l'utilisateur (fichier dans le stockage
--     privé S3 sous private/winai-sources/{userId}/, extraits indexés dans le RAG
--     personnel sous DocId), avec son état d'indexation ;
--   * droits ai.sources.max_files (nombre) et ai.sources.max_mb (volume, Mo) sur les
--     plans existants. Paliers validés par Georges : gratuit 10 / 30 Mo (valeur par
--     défaut du catalogue), Standard 30 / 100, Premium 60 / 250, Ultime 150 / 500 ;
--     un enfant couvert par Famille reçoit 60 / 250, par Famille+ 150 / 500.
-- Equivalent EF : Models/Entities/WinAiSource.cs, Data/ApplicationDbContext.WinAiSources.cs.
--
-- Prérequis : table Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "WinAiSources" (
    "Id"           SERIAL        PRIMARY KEY,
    "UserId"       INTEGER       NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Title"        VARCHAR(200)  NOT NULL,
    "FileName"     VARCHAR(255)  NOT NULL,
    "ContentType"  VARCHAR(100)  NOT NULL,
    "SizeBytes"    BIGINT        NOT NULL DEFAULT 0,
    "StorageRef"   VARCHAR(400)  NOT NULL,
    "DocId"        VARCHAR(120)  NOT NULL,
    "Status"       VARCHAR(20)   NOT NULL DEFAULT 'pending',
    "Error"        VARCHAR(500)  NULL,
    "ChunkCount"   INTEGER       NOT NULL DEFAULT 0,
    "CreatedAt"    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "IndexedAt"    TIMESTAMP WITH TIME ZONE NULL,
    "CheckedAt"    TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS "IX_WinAiSources_UserId" ON "WinAiSources"("UserId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_WinAiSources_DocId" ON "WinAiSources"("DocId");

-- ── Plafonds des sources sur les plans existants ─────────────────────────────
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125120000_WinAiSources') THEN
        RETURN; -- déjà posé une fois : les réglages admin ultérieurs priment.
    END IF;
    IF to_regclass('"PlanEntitlements"') IS NULL OR to_regclass('"PlanVersions"') IS NULL
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_name = 'PricingPlans' AND column_name = 'PlanKey') THEN
        RETURN; -- grille 9.7b pas encore posée : les valeurs par défaut du catalogue s'appliquent.
    END IF;
    INSERT INTO "PlanEntitlements" ("PlanVersionId", "Key", "Value")
    SELECT v."Id", k.key, k.value
      FROM "PlanVersions" v
      JOIN "PricingPlans" p ON p."Id" = v."PricingPlanId"
      JOIN (VALUES
            ('student-standard',    'ai.sources.max_files', '30'),
            ('student-standard',    'ai.sources.max_mb',    '100'),
            ('student-premium',     'ai.sources.max_files', '60'),
            ('student-premium',     'ai.sources.max_mb',    '250'),
            ('student-ultime',      'ai.sources.max_files', '150'),
            ('student-ultime',      'ai.sources.max_mb',    '500'),
            ('parent-famille',      'ai.sources.max_files', '60'),
            ('parent-famille',      'ai.sources.max_mb',    '250'),
            ('parent-famille-plus', 'ai.sources.max_files', '150'),
            ('parent-famille-plus', 'ai.sources.max_mb',    '500')
           ) AS k(plan_key, key, value) ON k.plan_key = p."PlanKey"
    ON CONFLICT ("PlanVersionId", "Key") DO NOTHING;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261125120000_WinAiSources', '8.0.0')
ON CONFLICT DO NOTHING;
