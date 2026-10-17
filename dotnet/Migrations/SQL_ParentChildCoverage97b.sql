-- Migration: ParentChildCoverage97b
-- Lot 9.7b, restes de livraison (étude 9.7b section 1.4) : choix par le
-- parent des enfants couverts par son plan (catalogue, bonus WinAI, alertes)
-- quand il a plus d'enfants liés et acceptés que le droit children.max.
--   * ParentChildCoverages : une ligne par enfant choisi, avec la date du
--     dernier choix (SelectedAt). Sans ligne, la règle par défaut s'applique :
--     les liens acceptés les plus anciens (comportement antérieur inchangé).
-- Equivalent EF : Models/Entities/ParentChildCoverage.cs,
-- Data/ApplicationDbContext.Subscriptions97b.cs.
--
-- Prérequis : la table historique "Users". Indépendant de l'ordre des autres
-- scripts SQL_*.sql (aucun script ne crée ni ne modifie cette table).
-- Idempotent. Additif : aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "ParentChildCoverages" (
    "Id"         SERIAL  PRIMARY KEY,
    "ParentId"   INTEGER NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "StudentId"  INTEGER NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "SelectedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ParentChildCoverages_ParentId_StudentId"
    ON "ParentChildCoverages"("ParentId", "StudentId");
CREATE INDEX IF NOT EXISTS "IX_ParentChildCoverages_StudentId"
    ON "ParentChildCoverages"("StudentId");

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261018090000_ParentChildCoverage97b', '8.0.0')
ON CONFLICT DO NOTHING;
