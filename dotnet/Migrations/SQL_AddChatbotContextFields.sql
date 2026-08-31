-- Migration: Enrichissement ChatbotContexts pour WinAI Phase 1
-- Ajoute PerformanceHistory (JSONB) et ForceLanguage (VARCHAR) à la table ChatbotContexts

ALTER TABLE "ChatbotContexts"
    ADD COLUMN IF NOT EXISTS "PerformanceHistory" JSONB,
    ADD COLUMN IF NOT EXISTS "ForceLanguage"      VARCHAR(10);

-- Ajoute QuizMistakes si pas encore présente (voir SQL_AddFavoriteCollections pour le pattern)
CREATE TABLE IF NOT EXISTS "QuizMistakes" (
    "Id"             SERIAL PRIMARY KEY,
    "UserId"         INTEGER NOT NULL,
    "QuizId"         INTEGER,
    "QuizAttemptId"  INTEGER,
    "Subject"        VARCHAR(100),
    "Question"       VARCHAR(1000) NOT NULL,
    "GivenAnswer"    VARCHAR(500),
    "CorrectAnswer"  VARCHAR(500),
    "IsResolved"     BOOLEAN NOT NULL DEFAULT FALSE,
    "ResolvedAt"     TIMESTAMP WITH TIME ZONE,
    "CreatedAt"      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_QuizMistakes_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_QuizMistakes_UserId_IsResolved"
    ON "QuizMistakes"("UserId", "IsResolved");
