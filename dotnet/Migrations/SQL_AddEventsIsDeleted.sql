-- Migration: AddEventsIsDeleted
-- Dette pré-existante (code initial, juin 2026) : l'entité Event.cs a
-- toujours déclaré IsDeleted, mais aucune migration n'a jamais créé la
-- colonne en base. Provoquait un 500 systématique sur
-- GET /api/student/events/upcoming (StudentController), confirmé dans les
-- logs de production du 2026-10-11 : "42703: column e.IsDeleted does not
-- exist".
-- Equivalent EF entity: Models/Entities/Event.cs (IsDeleted)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Additif uniquement : aucune colonne retirée ni renommée.

ALTER TABLE "Events" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT false;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011000000_AddEventsIsDeleted', '8.0.0')
ON CONFLICT DO NOTHING;
