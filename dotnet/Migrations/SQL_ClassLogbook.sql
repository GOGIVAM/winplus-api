-- Migration: ClassLogbook
-- Cahier de textes numérique : pour chaque classe d'établissement (StudyGroup),
-- l'enseignant affecté note ce qui a été fait en cours et le travail à faire.
-- Lu par l'établissement, les élèves de la classe et leurs parents.
-- Equivalent EF : Models/Entities/ClassLogbookEntry.cs, Data/ApplicationDbContext.ClassLogbook.cs.
--
-- Prérequis : tables StudyGroups, Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "ClassLogbookEntries" (
    "Id"            SERIAL       PRIMARY KEY,
    "StudyGroupId"  INTEGER      NOT NULL REFERENCES "StudyGroups"("Id") ON DELETE CASCADE,
    "AuthorUserId"  INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Subject"       VARCHAR(100) NOT NULL,
    "LessonDate"    DATE         NOT NULL,
    "Content"       TEXT         NOT NULL,
    "HomeworkText"  TEXT         NULL,
    "HomeworkDue"   DATE         NULL,
    "IsDeleted"     BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"     TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS "IX_ClassLogbookEntries_StudyGroupId_LessonDate"
    ON "ClassLogbookEntries"("StudyGroupId", "LessonDate" DESC);
