-- Migration: AddChatGroups
-- Module 7 — Messagerie (professeur_complete.md), US-MSG-03
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "ChatGroups" (
    "Id"                 SERIAL PRIMARY KEY,
    "CreatorId"          INTEGER NOT NULL,
    "Name"               VARCHAR(80) NOT NULL,
    "PhotoUrl"           VARCHAR(500),
    "IsAnnouncementOnly" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt"          TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_ChatGroups_Users_CreatorId"
        FOREIGN KEY ("CreatorId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "ChatGroupMembers" (
    "Id"          SERIAL PRIMARY KEY,
    "ChatGroupId" INTEGER NOT NULL,
    "UserId"      INTEGER NOT NULL,
    "Role"        VARCHAR(20) NOT NULL DEFAULT 'member',
    "JoinedAt"    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_ChatGroupMembers_ChatGroups_ChatGroupId"
        FOREIGN KEY ("ChatGroupId") REFERENCES "ChatGroups"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ChatGroupMembers_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ChatGroupMembers_Group_User" ON "ChatGroupMembers"("ChatGroupId", "UserId");

CREATE TABLE IF NOT EXISTS "ChatGroupMessages" (
    "Id"          SERIAL PRIMARY KEY,
    "ChatGroupId" INTEGER NOT NULL,
    "SenderId"    INTEGER NOT NULL,
    "Content"     VARCHAR(2000),
    "Type"        VARCHAR(20) NOT NULL DEFAULT 'text',
    "FileUrl"     VARCHAR(500),
    "FileName"    VARCHAR(255),
    "IsPinned"    BOOLEAN NOT NULL DEFAULT FALSE,
    "IsDeleted"   BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_ChatGroupMessages_ChatGroups_ChatGroupId"
        FOREIGN KEY ("ChatGroupId") REFERENCES "ChatGroups"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ChatGroupMessages_Users_SenderId"
        FOREIGN KEY ("SenderId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_ChatGroupMessages_Group_CreatedAt" ON "ChatGroupMessages"("ChatGroupId", "CreatedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907210000_AddChatGroups', '8.0.0')
ON CONFLICT DO NOTHING;
