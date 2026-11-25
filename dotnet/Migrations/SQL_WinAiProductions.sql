-- Migration: WinAiProductions
-- Atelier WinAI (25/11/2026) : productions générées pour l'élève à partir de ses sources
-- (fiches recto verso, script de résumé audio), et droit ai.feature.flashcards.
--   * Fiches recto verso : réservées par défaut aux plans Standard et au-dessus (valeur par
--     défaut du catalogue : false ; ouvertes ici sur Standard, Premium, Ultime, Famille,
--     Famille+). Réglable ensuite par l'administration.
--   * Résumé audio : inclus dans tous les plans, débité en jetons WinAI (pas de droit).
-- Equivalent EF : Models/Entities/WinAiProduction.cs, Data/ApplicationDbContext.WinAiProductions.cs.
--
-- Prérequis : table Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "WinAiProductions" (
    "Id"          SERIAL        PRIMARY KEY,
    "UserId"      INTEGER       NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Kind"        VARCHAR(20)   NOT NULL,
    "Title"       VARCHAR(200)  NOT NULL,
    "Subject"     VARCHAR(150)  NULL,
    "Topic"       VARCHAR(200)  NULL,
    "Level"       VARCHAR(100)  NULL,
    "ItemCount"   INTEGER       NOT NULL DEFAULT 0,
    "ContentJson" TEXT          NOT NULL DEFAULT '{}',
    "SourcesJson" TEXT          NOT NULL DEFAULT '{}',
    "CreatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_WinAiProductions_UserId_Kind_CreatedAt" ON "WinAiProductions"("UserId", "Kind", "CreatedAt");

-- ── Fiches recto verso sur les plans existants ───────────────────────────────
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261125180000_WinAiProductions') THEN
        RETURN; -- déjà posé une fois : les réglages admin ultérieurs priment.
    END IF;
    IF to_regclass('"PlanEntitlements"') IS NULL OR to_regclass('"PlanVersions"') IS NULL
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_name = 'PricingPlans' AND column_name = 'PlanKey') THEN
        RETURN; -- grille 9.7b pas encore posée : la valeur par défaut du catalogue s'applique.
    END IF;
    INSERT INTO "PlanEntitlements" ("PlanVersionId", "Key", "Value")
    SELECT v."Id", 'ai.feature.flashcards', 'true'
      FROM "PlanVersions" v
      JOIN "PricingPlans" p ON p."Id" = v."PricingPlanId"
     WHERE p."PlanKey" IN ('student-standard', 'student-premium', 'student-ultime', 'parent-famille', 'parent-famille-plus')
    ON CONFLICT ("PlanVersionId", "Key") DO NOTHING;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261125180000_WinAiProductions', '8.0.0')
ON CONFLICT DO NOTHING;
