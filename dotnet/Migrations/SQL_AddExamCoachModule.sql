-- Migration: AddExamCoachModule
-- Table de base jamais créée en production pour le module Exam Coach
-- (Models/Entities/ExamCoachPlan.cs, ExamCoachDayCompletion.cs,
-- Controllers/ExamCoachController.cs). SQL_AddExamCoachParentWatchMode.sql
-- fait un ALTER TABLE "ExamCoachPlans" en supposant la table existante,
-- mais aucun script ne la créait : en production, ExamWatchModeExpirationService
-- échouait en boucle sur "relation ExamCoachPlans does not exist".
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Doit s'exécuter avant SQL_AddExamCoachParentWatchMode.sql (ordre alphabétique
-- du nom de fichier : "Module" < "ParentWatchMode").

CREATE TABLE IF NOT EXISTS "ExamCoachPlans" (
    "Id"                    SERIAL PRIMARY KEY,
    "UserId"                INTEGER NOT NULL,
    "ExamType"              VARCHAR(100) NOT NULL,
    "ExamDate"              TIMESTAMP WITH TIME ZONE NOT NULL,
    "HoursPerDay"           REAL NOT NULL DEFAULT 2.0,
    "PlanJson"              JSONB NOT NULL DEFAULT '{}',
    "ConfidenceScore"       REAL NOT NULL DEFAULT 0,
    "CreatedAt"             TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "LastRecalibratedAt"    TIMESTAMP WITH TIME ZONE,
    "IsActive"              BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT "FK_ExamCoachPlans_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_ExamCoachPlans_UserId" ON "ExamCoachPlans"("UserId");
CREATE INDEX IF NOT EXISTS "IX_ExamCoachPlans_IsActive_ExamDate" ON "ExamCoachPlans"("IsActive", "ExamDate");

CREATE TABLE IF NOT EXISTS "ExamCoachDayCompletions" (
    "Id"            SERIAL PRIMARY KEY,
    "PlanId"        INTEGER NOT NULL,
    "DayNumber"     INTEGER NOT NULL,
    "QuizScore"     REAL,
    "CompletedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_ExamCoachDayCompletions_ExamCoachPlans_PlanId"
        FOREIGN KEY ("PlanId") REFERENCES "ExamCoachPlans"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExamCoachDayCompletions_PlanId_DayNumber"
    ON "ExamCoachDayCompletions"("PlanId", "DayNumber");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260916000000_AddExamCoachModule', '8.0.0')
ON CONFLICT DO NOTHING;
