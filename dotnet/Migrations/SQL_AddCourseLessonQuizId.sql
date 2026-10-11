-- Migration: AddCourseLessonQuizId
-- CourseLesson.QuizId (Models/Entities/Course.cs) a été ajouté côté C# (leçon de
-- type quiz rattachée à un quiz existant), mais aucune migration n'a jamais créé la
-- colonne correspondante. Résultat : tout ajout de leçon à une formation
-- (POST /api/teacher/courses/{id}/sections/{sId}/lessons) échouait en 500 avec
-- « 42703: column "QuizId" of relation "CourseLessons" does not exist ».
-- Colonne facultative, sans clé étrangère (le modèle EF n'en déclare pas).
-- Idempotent : peut être rejoué sans casser une base déjà à jour.

ALTER TABLE "CourseLessons" ADD COLUMN IF NOT EXISTS "QuizId" INTEGER NULL;
