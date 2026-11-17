-- Migration: CurriculumProgress
-- « Mon programme » : avancement de chaque élève dans le programme officiel
-- du secondaire (6e à Terminale).
--   * CurriculumSelections : la classe et la série suivies (une ligne par élève) ;
--   * CurriculumProgress   : l'état de chaque chapitre commencé (« doing » ou
--     « done ») ; un chapitre « à voir » n'a pas de ligne. ChapterKey est la clé
--     du référentiel côté site : « classe|matière|module|chapitre ».
-- Equivalent EF : Models/Entities/CurriculumProgress.cs, Data/ApplicationDbContext.Curriculum.cs.
--
-- Prérequis : table Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "CurriculumSelections" (
    "Id"        SERIAL      PRIMARY KEY,
    "UserId"    INTEGER     NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Level"     VARCHAR(10) NOT NULL,
    "Series"    VARCHAR(5)  NULL,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CurriculumSelections_UserId" ON "CurriculumSelections"("UserId");

CREATE TABLE IF NOT EXISTS "CurriculumProgress" (
    "Id"         SERIAL       PRIMARY KEY,
    "UserId"     INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "ChapterKey" VARCHAR(120) NOT NULL,
    "Status"     VARCHAR(10)  NOT NULL,
    "UpdatedAt"  TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CurriculumProgress_UserId_ChapterKey" ON "CurriculumProgress"("UserId", "ChapterKey");
