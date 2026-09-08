-- Migration: AddCourseChannelAndWinAIQA
-- Module 7 — Canal de formation + Q&A automatique WinAI (professeur_complete.md, 3C)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "CanalMessagerie" BOOLEAN NOT NULL DEFAULT FALSE;

-- ── CourseChannelMessages : fil de discussion par formation ────────────────
CREATE TABLE IF NOT EXISTS "CourseChannelMessages" (
    "Id"               SERIAL PRIMARY KEY,
    "CourseId"         INTEGER NOT NULL,
    "SenderUserId"     INTEGER,
    "IsAiGenerated"    BOOLEAN NOT NULL DEFAULT FALSE,
    "AiConfidence"     DOUBLE PRECISION,
    "TaggedProfessor"  BOOLEAN NOT NULL DEFAULT FALSE,
    "Content"          VARCHAR(2000) NOT NULL,
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_CourseChannelMessages_Courses_CourseId"
        FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CourseChannelMessages_Users_SenderUserId"
        FOREIGN KEY ("SenderUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS "IX_CourseChannelMessages_CourseId" ON "CourseChannelMessages"("CourseId");

-- ── WinAIInteractionLogs : journal des interactions Q&A par formation ──────
CREATE TABLE IF NOT EXISTS "WinAIInteractionLogs" (
    "Id"                  SERIAL PRIMARY KEY,
    "CourseId"            INTEGER NOT NULL,
    "StudentUserId"       INTEGER NOT NULL,
    "QuestionMessageId"   INTEGER,
    "Question"            VARCHAR(2000) NOT NULL,
    "Answer"              VARCHAR(4000),
    "Confidence"          DOUBLE PRECISION,
    "Action"              VARCHAR(20) NOT NULL DEFAULT 'answered',
    "CorrectedByTeacher"  BOOLEAN NOT NULL DEFAULT FALSE,
    "CorrectedAnswer"     VARCHAR(4000),
    "CorrectedAt"         TIMESTAMP WITH TIME ZONE,
    "CreatedAt"           TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_WinAIInteractionLogs_Courses_CourseId"
        FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_WinAIInteractionLogs_CourseId" ON "WinAIInteractionLogs"("CourseId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907190000_AddCourseChannelAndWinAIQA', '7.0.5')
ON CONFLICT DO NOTHING;
