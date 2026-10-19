-- Migration: InstitutionOnboarding
-- Espace institution, phase 2 (étape 7) : profil pédagogique de
-- l'établissement, renseigné à la première connexion du compte institution.
--   * Cycles          : primaire, secondaire, superieur, concours (séparés par des virgules) ;
--   * Subsystem       : fr | en | bilingue ;
--   * Teaching        : general | technique | both ;
--   * StudentsEstimate: nombre d'élèves annoncé ;
--   * Goals           : priorités choisies (séparées par des virgules) ;
--   * OnboardedAt     : date de fin de l'onboarding.
-- Equivalent EF : Models/Entities/Institution.cs.
--
-- Prérequis : table Institutions.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : colonnes nullables ajoutées, aucune donnée existante modifiée.
-- À exécuter AVANT le déploiement du code.

ALTER TABLE "Institutions" ADD COLUMN IF NOT EXISTS "Cycles"           VARCHAR(100) NULL;
ALTER TABLE "Institutions" ADD COLUMN IF NOT EXISTS "Subsystem"        VARCHAR(20)  NULL;
ALTER TABLE "Institutions" ADD COLUMN IF NOT EXISTS "Teaching"         VARCHAR(20)  NULL;
ALTER TABLE "Institutions" ADD COLUMN IF NOT EXISTS "StudentsEstimate" INTEGER      NULL;
ALTER TABLE "Institutions" ADD COLUMN IF NOT EXISTS "Goals"            VARCHAR(200) NULL;
ALTER TABLE "Institutions" ADD COLUMN IF NOT EXISTS "OnboardedAt"      TIMESTAMP WITH TIME ZONE NULL;
