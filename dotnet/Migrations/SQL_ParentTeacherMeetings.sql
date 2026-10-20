-- Migration: ParentTeacherMeetings
-- Rendez-vous parent-professeur : l'enseignant publie des créneaux, le parent
-- d'un élève de ses classes en réserve un (un créneau = un rendez-vous).
--   * TeacherMeetingSlots    : créneaux publiés (Mode : presentiel | visio | telephone) ;
--   * ParentTeacherMeetings  : réservations (Status : booked | cancelled).
-- Equivalent EF : Models/Entities/ParentTeacherMeeting.cs, Data/ApplicationDbContext.ParentTeacherMeetings.cs.
--
-- Prérequis : table Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "TeacherMeetingSlots" (
    "Id"              SERIAL       PRIMARY KEY,
    "TeacherUserId"   INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "StartsAt"        TIMESTAMP WITH TIME ZONE NOT NULL,
    "DurationMinutes" INTEGER      NOT NULL DEFAULT 15,
    "Mode"            VARCHAR(20)  NOT NULL DEFAULT 'presentiel',
    "Location"        VARCHAR(200) NULL,
    "IsDeleted"       BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_TeacherMeetingSlots_Teacher_StartsAt" ON "TeacherMeetingSlots"("TeacherUserId", "StartsAt");

CREATE TABLE IF NOT EXISTS "ParentTeacherMeetings" (
    "Id"            SERIAL       PRIMARY KEY,
    "SlotId"        INTEGER      NOT NULL REFERENCES "TeacherMeetingSlots"("Id") ON DELETE CASCADE,
    "TeacherUserId" INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "ParentUserId"  INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "ChildUserId"   INTEGER      NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Topic"         VARCHAR(500) NULL,
    "Status"        VARCHAR(20)  NOT NULL DEFAULT 'booked',
    "CancelledBy"   INTEGER      NULL,
    "CreatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CancelledAt"   TIMESTAMP WITH TIME ZONE NULL
);
-- Un seul rendez-vous actif par créneau.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ParentTeacherMeetings_Slot_Booked" ON "ParentTeacherMeetings"("SlotId") WHERE "Status" = 'booked';
CREATE INDEX IF NOT EXISTS "IX_ParentTeacherMeetings_Parent" ON "ParentTeacherMeetings"("ParentUserId");
