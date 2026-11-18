-- Migration: SubjectTitlesAndOAuthAccounts
-- 1. Connexion avec Google : table OAuthAccounts (lien entre un compte WinPlus
--    et son identifiant Google, « sub »). Le modèle existait côté code
--    (Models/Entities/OAuthAccount.cs, Data/ApplicationDbContext.cs) sans
--    aucune table en base.
-- 2. Titres d'épreuves saisis comme des noms de fichier (« Bac_D_Cameroun_2022 ») :
--    les _ deviennent des espaces, les espaces répétés sont réduits. Les
--    nouveaux titres sont nettoyés à l'enregistrement (Utils/ContentTitles.cs).
--
-- Prérequis : tables Users et Subjects.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif pour la table ; la reprise des titres ne touche que ceux qui
-- contiennent un _.
-- À exécuter AVANT le déploiement du code.

-- ── Comptes liés (Google) ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "OAuthAccounts" (
    "Id"              SERIAL        PRIMARY KEY,
    "UserId"          INTEGER       NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Provider"        VARCHAR(50)   NOT NULL,
    "ProviderUserId"  VARCHAR(255)  NOT NULL,
    "Email"           TEXT          NULL,
    "DisplayName"     VARCHAR(255)  NULL,
    "ProfileImageUrl" VARCHAR(500)  NULL,
    "ConnectedAt"     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    "DisconnectedAt"  TIMESTAMPTZ   NULL,
    "IsActive"        BOOLEAN       NOT NULL DEFAULT TRUE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_OAuthAccounts_Provider_ProviderUserId"
    ON "OAuthAccounts" ("Provider", "ProviderUserId");
CREATE INDEX IF NOT EXISTS "IX_OAuthAccounts_UserId"
    ON "OAuthAccounts" ("UserId");

-- ── Titres d'épreuves lisibles ───────────────────────────────────────────
UPDATE "Subjects"
SET "Title" = btrim(regexp_replace(regexp_replace("Title", '_+', ' ', 'g'), '\s{2,}', ' ', 'g'))
WHERE "Title" LIKE '%\_%' ESCAPE '\';
