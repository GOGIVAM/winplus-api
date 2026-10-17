-- Migration: Subscriptions97bGrid
-- Lot 9.7b, étape 5 (étude ETUDE_9_7B_ABONNEMENTS.md sections 1.3 à 1.5 et
-- 6.2 point 2, suivi 18.15, 18.19, 18.21) : grille de départ des abonnements.
--   * 12 familles de plans (3 publics × Gratuit et 3 paliers payants), clés
--     stables student-free … teacher-expert ;
--   * pour chacune une version 1 publiée, ses prix TTC (mensuel, trimestriel
--     = 3 mois × 0,90, annuel = 12 mois × 0,80) et ses droits typés ;
--   * archivage des 10 anciens plans de la grille A (Starter, Standard,
--     Premium, Annuel, Basique, Pro, Expert, Famille, Famille+, VIP) :
--     IsDeleted = TRUE (la colonne IsArchived n'existe pas en base), sans suppression, pour garder
--     l'historique des commandes et des abonnements qui les citent.
-- Les mêmes valeurs servent aux tests (tests/dotnet/Backend.Tests/Subscriptions97bTestGrid.cs).
--
-- Prérequis : SQL_Subscriptions97bSocle.sql (tables PlanVersions, PlanPrices,
-- PlanEntitlements, colonnes PlanKey, Audience, IsFreeTier, SortOrder).
-- Ordre conseillé : Socle, Branchement, Cycle, Institution97bLicenses, puis ce script.
-- À exécuter AVANT le déploiement du code (sans plan gratuit publié, les comptes
-- sans abonnement retombent sur les valeurs par défaut du catalogue des droits).
-- Le script manuel scripts/9.7b/migrate_subscriptions.sql (à lancer APRÈS le
-- déploiement) rattache ensuite les abonnements existants à cette grille.
--
-- Idempotent : une famille dont la clé existe déjà n'est jamais modifiée (les
-- réglages faits depuis l'écran admin des plans sont conservés) ; l'archivage
-- ne touche que les plans sans clé (grille A) encore actifs.

CREATE OR REPLACE FUNCTION pg_temp.grid97b_family(
    p_key TEXT, p_audience TEXT, p_name TEXT, p_free BOOLEAN, p_sort INTEGER,
    p_monthly NUMERIC, p_description TEXT, p_entitlements TEXT[])
RETURNS VOID LANGUAGE plpgsql AS $fn$
DECLARE
    v_plan    INTEGER;
    v_version INTEGER;
    i         INTEGER;
BEGIN
    IF EXISTS (SELECT 1 FROM "PricingPlans" WHERE "PlanKey" = p_key) THEN
        RETURN;
    END IF;

    INSERT INTO "PricingPlans" ("Name", "Category", "Price", "Currency", "Description",
                                "IsPopular", "IsDeleted", "CreatedAt",
                                "PlanKey", "Audience", "IsFreeTier", "SortOrder")
    VALUES (p_name, p_audience || 's', p_monthly, 'XAF', p_description,
            p_key IN ('student-premium', 'parent-famille', 'teacher-pro'), FALSE, NOW(),
            p_key, p_audience, p_free, p_sort)
    RETURNING "Id" INTO v_plan;

    INSERT INTO "PlanVersions" ("PricingPlanId", "VersionNumber", "Status", "Notes", "CreatedAt",
                                "PublishedAt", "ExistingSubscribersFrom", "IsImprovement")
    VALUES (v_plan, 1, 'published', 'Grille de départ 9.7b', NOW(), NOW(), NOW(), FALSE)
    RETURNING "Id" INTO v_version;

    IF NOT p_free THEN
        INSERT INTO "PlanPrices" ("PlanVersionId", "Period", "PriceTtc") VALUES
            (v_version, 'monthly',   p_monthly),
            (v_version, 'quarterly', ROUND(p_monthly * 3 * 0.9)),
            (v_version, 'yearly',    ROUND(p_monthly * 12 * 0.8));
    END IF;

    i := 1;
    WHILE i < array_length(p_entitlements, 1) LOOP
        INSERT INTO "PlanEntitlements" ("PlanVersionId", "Key", "Value")
        VALUES (v_version, p_entitlements[i], p_entitlements[i + 1]);
        i := i + 2;
    END LOOP;
END $fn$;

-- ── Archivage de la grille A ─────────────────────────────────────────────
-- Avant la création : un ancien plan ne peut pas gêner l'index des plans gratuits.
UPDATE "PricingPlans"
   SET "IsDeleted" = TRUE, "IsPopular" = FALSE, "UpdatedAt" = NOW()
 WHERE "PlanKey" IS NULL AND "IsDeleted" = FALSE;

-- ── Élève (étude 1.3) ────────────────────────────────────────────────────
SELECT pg_temp.grid97b_family('student-free', 'student', 'Gratuit', TRUE, 0, 0,
    'Pour découvrir WinPlus : aperçu du catalogue et WinAI au niveau de référence.',
    ARRAY['catalog.access','false', 'ai.reference_tokens','200000', 'ai.topup','true', 'ai.feature.chat','true',
          'ai.feature.attachments','false', 'ai.feature.error_explain','true', 'ai.feature.adaptive_quiz','true',
          'ai.feature.study_plan','false', 'ai.feature.study_session','false', 'ai.feature.exam_coach','false',
          'groups.max_owned','1', 'favorites.max','20']);
SELECT pg_temp.grid97b_family('student-standard', 'student', 'Standard', FALSE, 1, 2500,
    'Le catalogue complet, WinAI 4 fois plus, plan et sessions d''étude.',
    ARRAY['catalog.access','true', 'ai.reference_tokens','800000', 'ai.topup','true', 'ai.feature.chat','true',
          'ai.feature.attachments','true', 'ai.feature.error_explain','true', 'ai.feature.adaptive_quiz','true',
          'ai.feature.study_plan','true', 'ai.feature.study_session','true', 'ai.feature.exam_coach','false',
          'groups.max_owned','3', 'favorites.max','-1']);
SELECT pg_temp.grid97b_family('student-premium', 'student', 'Premium', FALSE, 2, 5000,
    'Tout le Standard, WinAI 10 fois plus et l''Exam Coach.',
    ARRAY['catalog.access','true', 'ai.reference_tokens','2000000', 'ai.topup','true', 'ai.feature.chat','true',
          'ai.feature.attachments','true', 'ai.feature.error_explain','true', 'ai.feature.adaptive_quiz','true',
          'ai.feature.study_plan','true', 'ai.feature.study_session','true', 'ai.feature.exam_coach','true',
          'groups.max_owned','10', 'favorites.max','-1']);
SELECT pg_temp.grid97b_family('student-ultime', 'student', 'Ultime', FALSE, 3, 7500,
    'Tout le Premium, WinAI 15 fois plus et des groupes illimités.',
    ARRAY['catalog.access','true', 'ai.reference_tokens','3000000', 'ai.topup','true', 'ai.feature.chat','true',
          'ai.feature.attachments','true', 'ai.feature.error_explain','true', 'ai.feature.adaptive_quiz','true',
          'ai.feature.study_plan','true', 'ai.feature.study_session','true', 'ai.feature.exam_coach','true',
          'groups.max_owned','-1', 'favorites.max','-1']);

-- ── Parent (étude 1.4, dotation 18.21 : 0 / 1 000 / 2 000) ───────────────
SELECT pg_temp.grid97b_family('parent-free', 'parent', 'Gratuit', TRUE, 0, 0,
    'Le suivi de base d''un enfant.',
    ARRAY['children.max','1', 'children.catalog','false', 'parent.monthly_credit_xaf','0', 'parent.realtime_alerts','false',
          'ai.reference_tokens','200000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.parent_advisor','true',
          'ai.parent_bonus_per_child','0', 'ai.parent_bonus_shares','0']);
SELECT pg_temp.grid97b_family('parent-basique', 'parent', 'Basique', FALSE, 1, 4000,
    'Un enfant suivi, avec le catalogue complet et un bonus WinAI.',
    ARRAY['children.max','1', 'children.catalog','true', 'parent.monthly_credit_xaf','0', 'parent.realtime_alerts','false',
          'ai.reference_tokens','400000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.parent_advisor','true',
          'ai.parent_bonus_per_child','400000', 'ai.parent_bonus_shares','1']);
SELECT pg_temp.grid97b_family('parent-famille', 'parent', 'Famille', FALSE, 2, 8000,
    'Jusqu''à 3 enfants, alertes en temps réel et dotation du portefeuille.',
    ARRAY['children.max','3', 'children.catalog','true', 'parent.monthly_credit_xaf','1000', 'parent.realtime_alerts','true',
          'ai.reference_tokens','600000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.parent_advisor','true',
          'ai.parent_bonus_per_child','400000', 'ai.parent_bonus_shares','3']);
SELECT pg_temp.grid97b_family('parent-famille-plus', 'parent', 'Famille+', FALSE, 3, 12000,
    'Jusqu''à 5 enfants, bonus WinAI renforcé et dotation de 2 000 XAF.',
    ARRAY['children.max','5', 'children.catalog','true', 'parent.monthly_credit_xaf','2000', 'parent.realtime_alerts','true',
          'ai.reference_tokens','1000000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.parent_advisor','true',
          'ai.parent_bonus_per_child','500000', 'ai.parent_bonus_shares','5']);

-- ── Professeur (étude 1.5, parts 18.21 : 70 / 70 / 75 / 80 %) ────────────
SELECT pg_temp.grid97b_family('teacher-free', 'teacher', 'Gratuit', TRUE, 0, 0,
    'Une classe, une formation publiée et 70 % sur le tutorat.',
    ARRAY['teacher.revenue_share','70', 'catalog.access','false', 'teacher.max_classes','1',
          'teacher.max_students_per_class','30', 'teacher.max_published_courses','1',
          'ai.reference_tokens','200000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.teacher_assist','true',
          'ai.feature.quiz_generation','false', 'ai.feature.correction_assist','false', 'ai.feature.course_builder','false']);
SELECT pg_temp.grid97b_family('teacher-fondateur', 'teacher', 'Fondateur', FALSE, 1, 5000,
    'Trois classes, le catalogue, la génération de quiz et la correction assistée.',
    ARRAY['teacher.revenue_share','70', 'catalog.access','true', 'teacher.max_classes','3',
          'teacher.max_students_per_class','50', 'teacher.max_published_courses','3',
          'ai.reference_tokens','1000000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.teacher_assist','true',
          'ai.feature.quiz_generation','true', 'ai.feature.correction_assist','true', 'ai.feature.course_builder','false']);
SELECT pg_temp.grid97b_family('teacher-pro', 'teacher', 'Pro', FALSE, 2, 10000,
    'Dix classes, 75 % sur le tutorat et la création de formation assistée.',
    ARRAY['teacher.revenue_share','75', 'catalog.access','true', 'teacher.max_classes','10',
          'teacher.max_students_per_class','100', 'teacher.max_published_courses','10',
          'ai.reference_tokens','2400000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.teacher_assist','true',
          'ai.feature.quiz_generation','true', 'ai.feature.correction_assist','true', 'ai.feature.course_builder','true']);
SELECT pg_temp.grid97b_family('teacher-expert', 'teacher', 'Expert', FALSE, 3, 15000,
    'Classes et formations illimitées, 80 % sur le tutorat.',
    ARRAY['teacher.revenue_share','80', 'catalog.access','true', 'teacher.max_classes','-1',
          'teacher.max_students_per_class','-1', 'teacher.max_published_courses','-1',
          'ai.reference_tokens','5000000', 'ai.topup','true', 'ai.feature.chat','true', 'ai.feature.teacher_assist','true',
          'ai.feature.quiz_generation','true', 'ai.feature.correction_assist','true', 'ai.feature.course_builder','true']);

DROP FUNCTION IF EXISTS pg_temp.grid97b_family(TEXT, TEXT, TEXT, BOOLEAN, INTEGER, NUMERIC, TEXT, TEXT[]);

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261017100000_Subscriptions97bGrid', '8.0.0')
ON CONFLICT DO NOTHING;
