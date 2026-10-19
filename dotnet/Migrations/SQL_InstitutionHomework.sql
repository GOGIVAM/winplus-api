-- Migration: InstitutionHomework
-- Espace institution, phase 2 (étape 3) : devoirs donnés par un établissement
-- à l'une de ses classes (StudyGroups créés par le compte institution).
-- Un devoir est toujours un quiz jouable : un quiz du catalogue, ou une
-- épreuve transformée en quiz (QuizService.GetOrCreateExamQuizAsync).
-- L'avancement est calculé à partir des QuizAttempts terminés des membres de
-- la classe après la date du devoir : aucune table de rendu supplémentaire.
-- Equivalent EF : Models/Entities/InstitutionHomework.cs,
-- Data/ApplicationDbContext.InstitutionHomework.cs.
--
-- Prérequis : tables Institutions, StudyGroups, Quizzes, Exams.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "InstitutionHomework" (
    "Id"              SERIAL       PRIMARY KEY,
    "InstitutionId"   INTEGER      NOT NULL REFERENCES "Institutions"("Id") ON DELETE CASCADE,
    "StudyGroupId"    INTEGER      NOT NULL REFERENCES "StudyGroups"("Id") ON DELETE CASCADE,
    "CreatedByUserId" INTEGER      NOT NULL,
    "QuizId"          INTEGER      NOT NULL REFERENCES "Quizzes"("Id") ON DELETE CASCADE,
    "ExamId"          INTEGER      NULL,
    "Title"           VARCHAR(200) NOT NULL,
    "Instructions"    TEXT         NULL,
    "DueDate"         TIMESTAMP WITH TIME ZONE NULL,
    "IsDeleted"       BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_InstitutionHomework_StudyGroupId" ON "InstitutionHomework"("StudyGroupId");
CREATE INDEX IF NOT EXISTS "IX_InstitutionHomework_InstitutionId_CreatedAt"
    ON "InstitutionHomework"("InstitutionId", "CreatedAt" DESC);
