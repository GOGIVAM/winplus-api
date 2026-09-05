-- ============================================================================
--  WinPlus  Focus Mode : table FocusSessions
--  Migration idempotente (IF NOT EXISTS).
-- ============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS "FocusSessions" (
    "Id"                       SERIAL PRIMARY KEY,
    "UserId"                   INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "PlannedDurationSeconds"   INTEGER      NOT NULL DEFAULT 1500,
    "ActualDurationSeconds"    INTEGER,
    "Label"                    VARCHAR(200),
    "StartedAt"                TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "CompletedAt"              TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS "IX_FocusSessions_UserId_StartedAt"
    ON "FocusSessions" ("UserId", "StartedAt");

COMMIT;
