-- Migration: AddConversationArchiving
-- Audit complet messagerie vs professeur_complete.md : "Swipe gauche →
-- archiver" (§ UX Messagerie mobile) — seule fonctionnalité de messagerie
-- confirmée absente sur les ~15 attendues. Idempotent.

CREATE TABLE IF NOT EXISTS "ArchivedConversations" (
    "Id"           SERIAL PRIMARY KEY,
    "UserId"       INTEGER NOT NULL,
    "OtherUserId"  INTEGER NOT NULL,
    "ArchivedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_ArchivedConversations_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArchivedConversations_UserId_OtherUserId"
    ON "ArchivedConversations"("UserId", "OtherUserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260908010000_AddConversationArchiving', '8.0.0')
ON CONFLICT DO NOTHING;
