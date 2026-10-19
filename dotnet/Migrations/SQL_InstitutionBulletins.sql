-- Migration: InstitutionBulletins
-- Espace institution, phase 2 (étape 4) : bulletin d'un élève sur une période,
-- calculé à partir de ses quiz, devoirs d'établissement et sessions d'étude,
-- complété d'une appréciation, puis partagé à l'élève et à ses parents.
-- Le contenu est figé au partage (DataJson) : le bulletin ne change plus
-- ensuite, même si de nouveaux quiz sont faits sur la période.
-- Equivalent EF : Models/Entities/InstitutionBulletin.cs,
-- Data/ApplicationDbContext.InstitutionBulletins.cs.
--
-- Prérequis : tables Institutions, Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "InstitutionBulletins" (
    "Id"              SERIAL       PRIMARY KEY,
    "InstitutionId"   INTEGER      NOT NULL REFERENCES "Institutions"("Id") ON DELETE CASCADE,
    "StudentId"       INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "CreatedByUserId" INTEGER      NOT NULL,
    "PeriodLabel"     VARCHAR(100) NOT NULL,
    "PeriodStart"     TIMESTAMP WITH TIME ZONE NOT NULL,
    "PeriodEnd"       TIMESTAMP WITH TIME ZONE NOT NULL,
    "Appreciation"    TEXT         NULL,
    "DataJson"        TEXT         NOT NULL,
    "SharedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "IsDeleted"       BOOLEAN      NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS "IX_InstitutionBulletins_StudentId" ON "InstitutionBulletins"("StudentId");
CREATE INDEX IF NOT EXISTS "IX_InstitutionBulletins_InstitutionId_SharedAt"
    ON "InstitutionBulletins"("InstitutionId", "SharedAt" DESC);
