-- Migration: AddCatalogCommissionSettings
-- Module 7 : grille de commission catalogue, modifiable par un administrateur
-- (pas un fichier de configuration). Ligne unique (Id=1) : bornes 10%-60%
-- imposées par le product owner, un administrateur peut ajuster MinRatePercent
-- et MaxRatePercent À L'INTÉRIEUR de cette fourchette, jamais en dehors
-- (contrôlé côté service, pas seulement en base).
-- Equivalent EF entity: Models/Entities/CatalogCommissionSettings.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "CatalogCommissionSettings" (
    "Id" SERIAL PRIMARY KEY,
    "MinRatePercent" NUMERIC(5,2) NOT NULL DEFAULT 10,
    "MaxRatePercent" NUMERIC(5,2) NOT NULL DEFAULT 60,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now(),
    "UpdatedByUserId" INTEGER NULL
);

INSERT INTO "CatalogCommissionSettings" ("Id", "MinRatePercent", "MaxRatePercent")
VALUES (1, 10, 60)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005120100_AddCatalogCommissionSettings', '8.0.0')
ON CONFLICT DO NOTHING;
