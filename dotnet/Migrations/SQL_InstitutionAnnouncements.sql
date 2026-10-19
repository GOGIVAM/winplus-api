-- Migration: InstitutionAnnouncements
-- Espace institution, phase 2 (étape 2) : annonces d'un établissement à ses
-- élèves et/ou aux parents de ses élèves, pour tout l'établissement ou une
-- seule classe (StudyGroups créés par le compte institution).
--   * Audience : students | parents | all ;
--   * Type     : info | important | urgent ;
--   * StudyGroupId NULL : tout l'établissement ;
--   * ExpiresAt NULL : visible jusqu'à suppression.
-- Destinataires calculés à l'envoi : élèves rattachés ET ayant accepté le
-- rattachement (InstitutionStudents.ConfirmationStatus = 'accepted'), et les
-- parents liés à ces élèves (ParentStudentLinks.Status = 'accepted').
-- Equivalent EF : Models/Entities/InstitutionAnnouncement.cs,
-- Data/ApplicationDbContext.InstitutionAnnouncements.cs.
--
-- Prérequis : tables Institutions, Users, StudyGroups.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "InstitutionAnnouncements" (
    "Id"             SERIAL       PRIMARY KEY,
    "InstitutionId"  INTEGER      NOT NULL REFERENCES "Institutions"("Id") ON DELETE CASCADE,
    "StudyGroupId"   INTEGER      NULL REFERENCES "StudyGroups"("Id") ON DELETE SET NULL,
    "AuthorUserId"   INTEGER      NOT NULL,
    "Audience"       VARCHAR(20)  NOT NULL DEFAULT 'all',
    "Type"           VARCHAR(20)  NOT NULL DEFAULT 'info',
    "Title"          VARCHAR(200) NOT NULL,
    "Content"        TEXT         NOT NULL,
    "RecipientCount" INTEGER      NOT NULL DEFAULT 0,
    "ExpiresAt"      TIMESTAMP WITH TIME ZONE NULL,
    "IsDeleted"      BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"      TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS "IX_InstitutionAnnouncements_InstitutionId_CreatedAt"
    ON "InstitutionAnnouncements"("InstitutionId", "CreatedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_InstitutionAnnouncements_StudyGroupId"
    ON "InstitutionAnnouncements"("StudyGroupId");
