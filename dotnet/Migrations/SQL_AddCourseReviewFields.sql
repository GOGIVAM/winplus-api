-- Migration: AddCourseReviewFields
-- L'entité Course (Models/Entities/Course.cs) déclare ReviewedBy et ReviewedAt
-- (validation admin d'une formation), mais aucune migration du dépôt ne
-- créait ces colonnes : toute requête chargeant un Course (ex. panier avec
-- une formation) échouait en 42703 "column c0.ReviewedAt does not exist".
-- Colonnes nullables, sans clé étrangère (même choix que RejectionReason).
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "ReviewedBy" INTEGER;
ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "ReviewedAt" TIMESTAMP WITH TIME ZONE;
