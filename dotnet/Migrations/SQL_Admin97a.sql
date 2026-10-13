-- Migration: Admin97a
-- Lot 9.7a (suivi Partie 18, points 18.1 à 18.9, décisions du 2026-10-13) :
--   * 18.3  journal d'audit des actions sensibles de l'administration (AdminAuditLogs) ;
--   * 18.2  réglages de plateforme persistés et typés (PlatformSettings) ;
--   * 18.5  décision de remboursement au portefeuille d'une commande catalogue
--           (CatalogRefundDecisions), figée à l'approbation ;
--   * 18.1  e-mails de masse envoyés en tâche de fond, avec rapport (AdminEmailCampaigns) ;
--   * 18.7  annonces : type et rôles visés enregistrés ; codes promo : périmètre
--           (catalogue / abonnements) ;
--   * avis de tuteur signalés : décision de modération (retiré / conservé).
-- Equivalent EF : Data/ApplicationDbContext.Admin.cs, Models/Entities/AdminAuditLog.cs,
-- PlatformSetting.cs, CatalogRefundDecision.cs, AdminEmailCampaign.cs,
-- Announcement.cs (Type, TargetRoles), PromoCode.cs (AppliesTo), TutorReview.cs.
--
-- Aucun prérequis d'ordre : le script ne dépend que de tables historiques
-- (Announcements, PromoCodes, TutorReviews), et peut passer avant ou après
-- n'importe quel autre script SQL_*.sql.
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée ; aucune donnée existante
-- modifiée (les annonces existantes gardent leur affichage : type déduit de
-- la priorité, comme le faisait l'écran ; les codes promo existants
-- s'appliquent au catalogue, seul usage possible jusqu'ici).
-- Longueurs des colonnes de statut : Status VARCHAR(20) (« completed » = 9),
-- ModerationDecision VARCHAR(20) (« removed » = 7), PaymentKind VARCHAR(20)
-- (« parent_wallet » = 13), AppliesTo VARCHAR(20) (« subscription » = 12),
-- Action VARCHAR(80) (codes de AdminAuditActions, 35 au plus).

-- ── Journal d'audit ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "AdminAuditLogs" (
    "Id"          BIGSERIAL     PRIMARY KEY,
    "ActorUserId" INTEGER       NULL,
    "ActorEmail"  VARCHAR(256)  NULL,
    "Action"      VARCHAR(80)   NOT NULL,
    "TargetType"  VARCHAR(50)   NULL,
    "TargetId"    VARCHAR(64)   NULL,
    "TargetLabel" VARCHAR(300)  NULL,
    "Before"      TEXT          NULL,
    "After"       TEXT          NULL,
    "Reason"      VARCHAR(1000) NULL,
    "IpAddress"   VARCHAR(64)   NULL,
    "CreatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_AdminAuditLogs_CreatedAt" ON "AdminAuditLogs"("CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_AdminAuditLogs_ActorUserId_CreatedAt" ON "AdminAuditLogs"("ActorUserId", "CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_AdminAuditLogs_Action_CreatedAt" ON "AdminAuditLogs"("Action", "CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_AdminAuditLogs_TargetType_TargetId" ON "AdminAuditLogs"("TargetType", "TargetId");

-- Le journal n'est jamais modifié ni supprimé par l'application : un
-- déclencheur refuse UPDATE et DELETE (une purge éventuelle passera par un
-- script dédié qui le désactive explicitement).
CREATE OR REPLACE FUNCTION "fn_AdminAuditLogs_ReadOnly"() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'AdminAuditLogs est en ajout seul (lot 9.7a).';
END;
$$;
DROP TRIGGER IF EXISTS "TRG_AdminAuditLogs_ReadOnly" ON "AdminAuditLogs";
CREATE TRIGGER "TRG_AdminAuditLogs_ReadOnly"
    BEFORE UPDATE OR DELETE ON "AdminAuditLogs"
    FOR EACH ROW EXECUTE PROCEDURE "fn_AdminAuditLogs_ReadOnly"();

-- ── Réglages de plateforme ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "PlatformSettings" (
    "Key"             VARCHAR(100) PRIMARY KEY,
    "Value"           TEXT         NOT NULL DEFAULT '',
    "ValueType"       VARCHAR(20)  NOT NULL DEFAULT 'string',
    "Description"     VARCHAR(500) NULL,
    "UpdatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedByUserId" INTEGER      NULL
);

-- ── Remboursement catalogue au portefeuille (18.5) ───────────────────────
CREATE TABLE IF NOT EXISTS "CatalogRefundDecisions" (
    "OrderId"         INTEGER       PRIMARY KEY,
    "BuyerUserId"     INTEGER       NOT NULL,
    "OrderTotalXaf"   NUMERIC(12,2) NOT NULL,
    "BalancePartXaf"  NUMERIC(12,2) NOT NULL DEFAULT 0,
    "FeeXaf"          NUMERIC(12,2) NOT NULL DEFAULT 0,
    "RefundXaf"       NUMERIC(12,2) NOT NULL,
    "PaymentKind"     VARCHAR(20)   NOT NULL DEFAULT 'mobile_money',
    "DecidedByUserId" INTEGER       NULL,
    "DecidedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "CK_CatalogRefundDecisions_Amounts" CHECK ("RefundXaf" >= 0 AND "FeeXaf" >= 0 AND "BalancePartXaf" >= 0)
);

-- ── E-mails de masse (envoi en tâche de fond) ────────────────────────────
CREATE TABLE IF NOT EXISTS "AdminEmailCampaigns" (
    "Id"              SERIAL       PRIMARY KEY,
    "Target"          VARCHAR(20)  NOT NULL DEFAULT 'all',
    "CustomEmail"     VARCHAR(256) NULL,
    "Subject"         VARCHAR(300) NOT NULL DEFAULT '',
    "Body"            TEXT         NOT NULL DEFAULT '',
    "Status"          VARCHAR(20)  NOT NULL DEFAULT 'queued',
    "RecipientCount"  INTEGER      NOT NULL DEFAULT 0,
    "SentCount"       INTEGER      NOT NULL DEFAULT 0,
    "FailedCount"     INTEGER      NOT NULL DEFAULT 0,
    "OptedOutCount"   INTEGER      NOT NULL DEFAULT 0,
    "Error"           VARCHAR(500) NULL,
    "CreatedByUserId" INTEGER      NULL,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "StartedAt"       TIMESTAMP WITH TIME ZONE NULL,
    "FinishedAt"      TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS "IX_AdminEmailCampaigns_Status_CreatedAt" ON "AdminEmailCampaigns"("Status", "CreatedAt");

-- ── Annonces : type et rôles visés (18.7) ───────────────────────────────
ALTER TABLE "Announcements" ADD COLUMN IF NOT EXISTS "Type"        VARCHAR(20)  NOT NULL DEFAULT 'info';
ALTER TABLE "Announcements" ADD COLUMN IF NOT EXISTS "TargetRoles" VARCHAR(100) NULL;
-- Annonces existantes : type tel que l'écran le déduisait de la priorité
-- (0 info, 1 succès, 2 avertissement, 3 critique), une seule fois.
UPDATE "Announcements"
   SET "Type" = CASE "Priority" WHEN 1 THEN 'success' WHEN 2 THEN 'warn' WHEN 3 THEN 'critical' ELSE 'info' END
 WHERE "Type" = 'info' AND "Priority" IN (1, 2, 3)
   AND NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013150000_Admin97a');

-- ── Codes promo : périmètre (18.7) ──────────────────────────────────────
ALTER TABLE "PromoCodes" ADD COLUMN IF NOT EXISTS "AppliesTo" VARCHAR(20) NOT NULL DEFAULT 'catalog';
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_PromoCodes_AppliesTo') THEN
        ALTER TABLE "PromoCodes" ADD CONSTRAINT "CK_PromoCodes_AppliesTo"
            CHECK ("AppliesTo" IN ('catalog', 'subscription', 'all'));
    END IF;
END $$;

-- ── Avis de tuteur signalés : décision de modération ─────────────────────
ALTER TABLE "TutorReviews" ADD COLUMN IF NOT EXISTS "IsHiddenByModeration" BOOLEAN     NOT NULL DEFAULT FALSE;
ALTER TABLE "TutorReviews" ADD COLUMN IF NOT EXISTS "ModerationDecision"   VARCHAR(20) NULL;
ALTER TABLE "TutorReviews" ADD COLUMN IF NOT EXISTS "ModeratedByUserId"    INTEGER     NULL;
ALTER TABLE "TutorReviews" ADD COLUMN IF NOT EXISTS "ModeratedAt"          TIMESTAMP WITH TIME ZONE NULL;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261013150000_Admin97a', '8.0.0')
ON CONFLICT DO NOTHING;
