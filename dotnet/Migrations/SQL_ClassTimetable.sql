-- Migration: ClassTimetable
-- Emploi du temps des classes d'établissement (StudyGroups) : un créneau par
-- cours hebdomadaire (jour, heure de début et de fin, matière, enseignant, salle).
-- Les élèves de la classe et l'enseignant reçoivent un rappel avant le cours
-- (ClassTimetableReminderService).
--   * DayOfWeek : 1 = lundi … 7 = dimanche ;
--   * StartTime / EndTime : « HH:mm », heure du Cameroun (UTC+1).
-- Equivalent EF : Models/Entities/ClassTimetableSlot.cs, Data/ApplicationDbContext.ClassTimetable.cs.
--
-- Prérequis : tables StudyGroups, Users.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

CREATE TABLE IF NOT EXISTS "ClassTimetableSlots" (
    "Id"            SERIAL       PRIMARY KEY,
    "StudyGroupId"  INTEGER      NOT NULL REFERENCES "StudyGroups"("Id") ON DELETE CASCADE,
    "DayOfWeek"     INTEGER      NOT NULL,
    "StartTime"     VARCHAR(5)   NOT NULL,
    "EndTime"       VARCHAR(5)   NOT NULL,
    "Subject"       VARCHAR(100) NOT NULL,
    "TeacherUserId" INTEGER      NULL REFERENCES "Users"("Id") ON DELETE SET NULL,
    "Room"          VARCHAR(50)  NULL,
    "CreatedAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_ClassTimetableSlots_StudyGroupId" ON "ClassTimetableSlots"("StudyGroupId");
CREATE INDEX IF NOT EXISTS "IX_ClassTimetableSlots_DayOfWeek" ON "ClassTimetableSlots"("DayOfWeek");
