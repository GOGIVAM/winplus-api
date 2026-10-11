-- Migration: AddCourseEnrollmentOrderIdLessonProgressCreatedAt
-- Deux propriétés de Models/Entities/Course.cs n'ont jamais eu de colonne en base :
--   * CourseEnrollment.OrderId : toute inscription à une formation
--     (POST /api/courses/{id}/enroll) échouait en 500
--     (42703 : column "OrderId" of relation "CourseEnrollments" does not exist) ;
--   * LessonProgress.CreatedAt : même erreur à l'enregistrement de la progression.
-- OrderId : facultatif, sans clé étrangère (le modèle EF n'en déclare pas).
-- CreatedAt : obligatoire côté C# (initialisé à DateTime.UtcNow), défaut now() en base
-- pour les lignes existantes.
-- Idempotent : peut être rejoué sans casser une base déjà à jour.

ALTER TABLE "CourseEnrollments" ADD COLUMN IF NOT EXISTS "OrderId" INTEGER NULL;
ALTER TABLE "LessonProgress" ADD COLUMN IF NOT EXISTS "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now();
