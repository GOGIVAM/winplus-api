-- Migration: Visio
-- Cours en direct sur la visio WinPlus (lot 4 du plan temps réel, 24/11/2026).
--   * VisioPresences       : chaque passage dans la salle d'une séance (arrivée, dernier signal, départ),
--                            pour l'appel de l'animateur ;
--   * VisioEnregistrements : enregistrements déposés par l'animateur dans le stockage privé S3.
-- Type = 'seance' (table Sessions) ou 'tutorat' (table TutorBookings) ; RefId = Id dans cette table.
-- Equivalent EF : Models/Entities/Visio.cs, Data/ApplicationDbContext.Visio.cs.
--
-- Prérequis : table Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "VisioPresences" (
    "Id"         SERIAL      PRIMARY KEY,
    "Type"       VARCHAR(10) NOT NULL,
    "RefId"      INTEGER     NOT NULL,
    "UserId"     INTEGER     NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "IsHost"     BOOLEAN     NOT NULL DEFAULT FALSE,
    "JoinedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastSeenAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LeftAt"     TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS "IX_VisioPresences_Type_RefId" ON "VisioPresences"("Type", "RefId");
CREATE INDEX IF NOT EXISTS "IX_VisioPresences_UserId" ON "VisioPresences"("UserId");

CREATE TABLE IF NOT EXISTS "VisioEnregistrements" (
    "Id"               SERIAL       PRIMARY KEY,
    "Type"             VARCHAR(10)  NOT NULL,
    "RefId"            INTEGER      NOT NULL,
    "UploadedByUserId" INTEGER      NOT NULL,
    "StorageKey"       VARCHAR(300) NOT NULL,
    "FileName"         VARCHAR(200) NOT NULL,
    "ContentType"      VARCHAR(100) NOT NULL DEFAULT 'video/webm',
    "SizeBytes"        BIGINT       NOT NULL DEFAULT 0,
    "CreatedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "IsDeleted"        BOOLEAN      NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS "IX_VisioEnregistrements_Type_RefId" ON "VisioEnregistrements"("Type", "RefId");
