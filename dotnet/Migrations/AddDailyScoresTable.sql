-- Migration: Add DailyScores table
-- Stores one aggregated score row per user per day for the AnalysisChart
-- Run after SQL_Migration_Tables.sql

CREATE TABLE IF NOT EXISTS "DailyScores" (
    "Id"           SERIAL          PRIMARY KEY,
    "UserId"       INTEGER         NOT NULL,
    "Date"         DATE            NOT NULL,
    "AverageScore" DECIMAL(5,2)    NOT NULL,
    "QuizCount"    INTEGER         NOT NULL DEFAULT 1,
    "SubjectId"    INTEGER         NULL,
    "CreatedAt"    TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    "UpdatedAt"    TIMESTAMPTZ     NOT NULL DEFAULT NOW()
);

-- Unique constraint: one row per (user, day)
CREATE UNIQUE INDEX IF NOT EXISTS "ix_daily_scores_user_date"
    ON "DailyScores" ("UserId", "Date");

-- Index for temporal queries by user
CREATE INDEX IF NOT EXISTS "ix_daily_scores_user_id"
    ON "DailyScores" ("UserId");
