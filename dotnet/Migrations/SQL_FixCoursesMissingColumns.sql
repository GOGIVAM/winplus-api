-- Migration: FixCoursesMissingColumns
-- "Courses" n'a jamais été créée par une migration EF (aucun fichier de ce
-- dépôt ne contient "CREATE TABLE Courses" ni la moindre migration EF pour
-- cette entité — la table existe en prod mais a été créée hors suivi de
-- version). RejectionReason (AdminCourseController.cs:71, TeacherCourseController.cs)
-- et PublishedAt (AdminCourseController.cs:71,152) sont lus/écrits par le code
-- C# sans qu'aucun script ne les ait jamais ajoutés à la table réelle — un
-- commentaire dans Course.cs référence même un "sql/003_add_courses_publishedat.sql"
-- introuvable dans ce dépôt. C'est la cause probable du 500 récurrent sur
-- GET /api/teacher/courses (et potentiellement des endpoints admin équivalents) :
-- la requête échoue dès qu'elle touche une colonne absente en base.
-- Idempotent : peut être rejoué sans casser une base déjà à jour.

ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "RejectionReason" VARCHAR(1000);
ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "PublishedAt" TIMESTAMP WITH TIME ZONE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260911120000_FixCoursesMissingColumns', '8.0.0')
ON CONFLICT DO NOTHING;
