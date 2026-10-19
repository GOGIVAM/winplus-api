-- Migration: InstitutionMockExams
-- Espace institution, phase 2 (étape 6) : examens blancs. Un quiz (ou une
-- épreuve transformée en quiz) donné à une ou plusieurs classes, ouvert sur
-- une fenêtre de dates, avec une durée. Seule la première tentative terminée
-- de l'élève dans la fenêtre et dans la durée compte ; le classement est
-- calculé sur les QuizAttempts (aucune table de copies supplémentaire).
--   * GroupIds : identifiants des classes (StudyGroups), séparés par des virgules ;
--   * ResultsPublishedAt : date de publication des résultats aux élèves.
-- Equivalent EF : Models/Entities/InstitutionMockExam.cs,
-- Data/ApplicationDbContext.InstitutionMockExams.cs.
--
-- Prérequis : tables Institutions, Quizzes.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "InstitutionMockExams" (
    "Id"                 SERIAL       PRIMARY KEY,
    "InstitutionId"      INTEGER      NOT NULL REFERENCES "Institutions"("Id") ON DELETE CASCADE,
    "CreatedByUserId"    INTEGER      NOT NULL,
    "QuizId"             INTEGER      NOT NULL REFERENCES "Quizzes"("Id") ON DELETE CASCADE,
    "ExamId"             INTEGER      NULL,
    "Title"              VARCHAR(200) NOT NULL,
    "GroupIds"           VARCHAR(500) NOT NULL,
    "OpensAt"            TIMESTAMP WITH TIME ZONE NOT NULL,
    "ClosesAt"           TIMESTAMP WITH TIME ZONE NOT NULL,
    "DurationMinutes"    INTEGER      NOT NULL DEFAULT 60,
    "ResultsPublishedAt" TIMESTAMP WITH TIME ZONE NULL,
    "IsDeleted"          BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"          TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_InstitutionMockExams_InstitutionId_OpensAt"
    ON "InstitutionMockExams"("InstitutionId", "OpensAt" DESC);
