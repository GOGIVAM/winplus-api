-- Migration: AddParentAlertDedupKey
-- Partie 14.1 (décisions produit du 2026-10-08)  Proactivité du conseiller parent par
-- alertes ciblées sur seuils (ParentAdvisorThresholdService). Ajoute une clé de
-- déduplication stable par évènement précis (mois civil pour une baisse de score, id de
-- devoir pour un retard, date d'examen pour une veille d'examen), pour que le même
-- franchissement de seuil ne déclenche jamais deux fois la même alerte/notification.
-- NULL pour les alertes de l'ancien flux à la volée (parent_alert_routes.py), qui garde
-- sa propre déduplication 24h existante, inchangée.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "ParentAlerts" ADD COLUMN IF NOT EXISTS "DedupKey" VARCHAR(100) NULL;

-- Une seule ligne par évènement précis (partiel : ne contraint pas les alertes historiques
-- sans DedupKey).
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ParentAlerts_ParentId_ChildId_Type_DedupKey"
    ON "ParentAlerts" ("ParentId", "ChildId", "Type", "DedupKey")
    WHERE "DedupKey" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261008120000_AddParentAlertDedupKey', '8.0.0')
ON CONFLICT DO NOTHING;
