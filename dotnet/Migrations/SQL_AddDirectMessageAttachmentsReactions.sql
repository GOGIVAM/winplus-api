-- Migration: AddDirectMessageAttachmentsReactions
-- Module 7 — Messagerie directe : pièces jointes, citation, réactions,
-- suppression, messages programmés (professeur_complete.md, 3B).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "DirectMessages" ALTER COLUMN "Content" DROP NOT NULL;
ALTER TABLE "DirectMessages" ADD COLUMN IF NOT EXISTS "Type" VARCHAR(20) NOT NULL DEFAULT 'text';
ALTER TABLE "DirectMessages" ADD COLUMN IF NOT EXISTS "FileUrl" VARCHAR(500);
ALTER TABLE "DirectMessages" ADD COLUMN IF NOT EXISTS "FileName" VARCHAR(255);
ALTER TABLE "DirectMessages" ADD COLUMN IF NOT EXISTS "ReplyToMessageId" INTEGER;
ALTER TABLE "DirectMessages" ADD COLUMN IF NOT EXISTS "ScheduledSendAt" TIMESTAMP WITH TIME ZONE;
ALTER TABLE "DirectMessages" ADD COLUMN IF NOT EXISTS "ScheduledNotificationSent" BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE "DirectMessages" ADD COLUMN IF NOT EXISTS "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE "DirectMessages" DROP CONSTRAINT IF EXISTS "FK_DirectMessages_DirectMessages_ReplyToMessageId";
ALTER TABLE "DirectMessages" ADD CONSTRAINT "FK_DirectMessages_DirectMessages_ReplyToMessageId"
    FOREIGN KEY ("ReplyToMessageId") REFERENCES "DirectMessages"("Id") ON DELETE SET NULL;

-- ── DirectMessageReactions : une réaction par utilisateur et par message ───
CREATE TABLE IF NOT EXISTS "DirectMessageReactions" (
    "Id"               SERIAL PRIMARY KEY,
    "DirectMessageId"  INTEGER NOT NULL,
    "UserId"           INTEGER NOT NULL,
    "Emoji"            VARCHAR(8) NOT NULL DEFAULT '👍',
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_DirectMessageReactions_DirectMessages_DirectMessageId"
        FOREIGN KEY ("DirectMessageId") REFERENCES "DirectMessages"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_DirectMessageReactions_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_DirectMessageReactions_DirectMessageId_UserId"
    ON "DirectMessageReactions"("DirectMessageId", "UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907180000_AddDirectMessageAttachmentsReactions', '7.0.5')
ON CONFLICT DO NOTHING;
