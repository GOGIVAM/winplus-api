-- ════════════════════════════════════════════════════════════════════════════
-- scripts/9.7b/migrate_subscriptions.sql : migration des abonnements (lot 9.7b)
-- Étude ETUDE_9_7B_ABONNEMENTS.md section 6, décisions 18.12, 18.15 et 18.21.
--
-- SCRIPT MANUEL, hors dotnet/Migrations : à lancer par un administrateur,
-- APRÈS le déploiement du code 9.7b et APRÈS les scripts SQL, dans cet ordre :
--   SQL_Subscriptions97bSocle.sql, SQL_Subscriptions97bBranchement.sql,
--   SQL_Subscriptions97bCycle.sql, SQL_Institution97bLicenses.sql,
--   SQL_Subscriptions97bGrid.sql.
-- Ne jamais le lancer sans avoir fait le relevé (étape 6.2 point 1) et une
-- sauvegarde de la base :
--   psql -U <user> -d winplus -v ON_ERROR_STOP=1 -f scripts/9.7b/migrate_subscriptions.sql
--
-- Idempotent : rejoué sur une base déjà migrée, il ne change rien et
-- n'ajoute aucune ligne de journal (chaque modification n'est faite, et
-- journalisée, que si l'état cible n'est pas déjà atteint). Tout se fait
-- dans une seule transaction : en cas d'erreur, rien n'est appliqué.
-- Chaque modification est inscrite dans AdminAuditLogs avec le motif
-- « migration 9.7b » (étude 6.2 point 7).
--
-- Ce que fait le script :
--   1. statuts hérités hors du jeu autorisé → « cancelled » ;
--   2. correspondance des abonnements de la grille A vers la nouvelle grille
--      (étude 6.2 point 3) : même EndDate, sinon 1 mois ; un plan sans
--      équivalent payant (Starter, Basique professeur) ou destiné à un autre
--      public que le rôle du compte est résilié ;
--   3. un seul abonnement en vigueur par compte : le plus récent est gardé ;
--   4. comptes de référence 30, 26, 50, 40, 41, 42, 43, 19, 21 (étude 6.2 point 4) ;
--   5. index unique « un abonnement en vigueur par compte » et contrainte de
--      statut, s'ils manquent encore ;
--   6. avis de vérification (soldes des comptes 19, 30 et 26).
-- Ce qu'il ne fait pas : créditer un portefeuille (les soldes de test de
-- l'étude, 35 000 pour 30 et 75 000 pour 26, sont à poser par l'écran admin
-- de crédit si besoin, pour garder un journal de portefeuille exact) ; les
-- formations (IsIncludedInSub, étude 6.2 point 5) relèvent du chantier B.
-- ════════════════════════════════════════════════════════════════════════════

\set ON_ERROR_STOP on
BEGIN;

-- ── Prérequis ────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF (SELECT COUNT(*) FROM "PricingPlans" p
          JOIN "PlanVersions" v ON v."PricingPlanId" = p."Id" AND v."Status" = 'published'
         WHERE p."PlanKey" IN ('student-free','student-standard','student-premium','student-ultime',
                               'parent-free','parent-basique','parent-famille','parent-famille-plus',
                               'teacher-free','teacher-fondateur','teacher-pro','teacher-expert')) < 12 THEN
        RAISE EXCEPTION 'Grille 9.7b incomplète : lancer SQL_Subscriptions97bGrid.sql avant ce script.';
    END IF;
    IF to_regclass('"InstitutionLicenses"') IS NULL THEN
        RAISE EXCEPTION 'Table InstitutionLicenses absente : lancer SQL_Institution97bLicenses.sql avant ce script.';
    END IF;
END $$;

-- ── Outils ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION pg_temp.m97b_audit(p_action TEXT, p_type TEXT, p_id TEXT, p_label TEXT, p_before TEXT, p_after TEXT)
RETURNS VOID LANGUAGE sql AS $fn$
    INSERT INTO "AdminAuditLogs" ("ActorUserId", "ActorEmail", "Action", "TargetType", "TargetId", "TargetLabel",
                                  "Before", "After", "Reason", "IpAddress", "CreatedAt")
    VALUES (NULL, 'migration-9.7b', p_action, p_type, p_id, p_label, p_before, p_after, 'migration 9.7b', NULL, NOW());
$fn$;

-- Famille et version publiée d'une clé de plan.
CREATE OR REPLACE FUNCTION pg_temp.m97b_plan(p_key TEXT, OUT plan_id INTEGER, OUT version_id INTEGER, OUT audience TEXT, OUT name TEXT)
LANGUAGE sql AS $fn$
    SELECT p."Id", v."Id", p."Audience", p."Name"
      FROM "PricingPlans" p
      JOIN "PlanVersions" v ON v."PricingPlanId" = p."Id" AND v."Status" = 'published'
     WHERE p."PlanKey" = p_key;
$fn$;

CREATE OR REPLACE FUNCTION pg_temp.m97b_in_force(p_status TEXT, p_active BOOLEAN, p_deleted BOOLEAN)
RETURNS BOOLEAN LANGUAGE sql IMMUTABLE AS $fn$
    SELECT p_active AND NOT p_deleted AND LOWER(p_status) IN ('active', 'trialing', 'past_due');
$fn$;

-- Résilie un abonnement en vigueur (immédiatement) et le journalise.
CREATE OR REPLACE FUNCTION pg_temp.m97b_cancel(p_sub INTEGER, p_why TEXT)
RETURNS VOID LANGUAGE plpgsql AS $fn$
DECLARE s RECORD;
BEGIN
    SELECT * INTO s FROM "Subscriptions" WHERE "Id" = p_sub;
    IF NOT FOUND OR NOT pg_temp.m97b_in_force(s."Status", s."IsActive", s."IsDeleted") THEN RETURN; END IF;
    UPDATE "Subscriptions"
       SET "Status" = 'cancelled', "IsActive" = FALSE, "AutoRenew" = FALSE, "CancelledAt" = NOW(), "UpdatedAt" = NOW()
     WHERE "Id" = p_sub;
    PERFORM pg_temp.m97b_audit('user.subscription_granted', 'user', s."UserId"::TEXT, 'abonnement ' || p_sub,
        format('plan %s, statut %s, fin %s', s."PricingPlanId", s."Status", s."EndDate"),
        'résilié : ' || p_why);
END $fn$;

-- Place un compte sur un plan payant de la nouvelle grille. Si un abonnement
-- en vigueur sur la même famille existe déjà, seuls la période et le
-- renouvellement automatique sont alignés (sa date de fin est conservée) ;
-- sinon les autres abonnements en vigueur sont résiliés et un abonnement
-- est créé, de NOW() à p_end.
CREATE OR REPLACE FUNCTION pg_temp.m97b_set_plan(p_user INTEGER, p_key TEXT, p_period TEXT, p_end TIMESTAMPTZ, p_auto BOOLEAN)
RETURNS VOID LANGUAGE plpgsql AS $fn$
DECLARE
    pl RECORD; cur RECORD; price NUMERIC; new_id INTEGER;
BEGIN
    SELECT * INTO pl FROM pg_temp.m97b_plan(p_key);
    SELECT "PriceTtc" INTO price FROM "PlanPrices" WHERE "PlanVersionId" = pl.version_id AND "Period" = p_period;

    SELECT * INTO cur FROM "Subscriptions"
     WHERE "UserId" = p_user AND "PricingPlanId" = pl.plan_id
       AND pg_temp.m97b_in_force("Status", "IsActive", "IsDeleted")
     ORDER BY "Id" DESC LIMIT 1;

    IF FOUND THEN
        IF cur."BillingPeriod" IS DISTINCT FROM p_period OR cur."AutoRenew" IS DISTINCT FROM p_auto
           OR cur."PlanVersionId" IS DISTINCT FROM pl.version_id OR cur."CancelAtPeriodEnd" THEN
            UPDATE "Subscriptions"
               SET "BillingPeriod" = p_period, "AutoRenew" = p_auto, "CancelAtPeriodEnd" = FALSE,
                   "PlanVersionId" = pl.version_id, "Audience" = pl.audience, "PlanName" = pl.name, "UpdatedAt" = NOW()
             WHERE "Id" = cur."Id";
            PERFORM pg_temp.m97b_audit('user.subscription_granted', 'user', p_user::TEXT, 'abonnement ' || cur."Id",
                format('%s, %s, renouvellement %s', cur."PlanName", cur."BillingPeriod", cur."AutoRenew"),
                format('%s, %s, renouvellement %s', p_key, p_period, p_auto));
        END IF;
        -- Les autres abonnements en vigueur du compte sont résiliés.
        PERFORM pg_temp.m97b_cancel(s."Id", 'un seul abonnement en vigueur par compte')
           FROM "Subscriptions" s
          WHERE s."UserId" = p_user AND s."Id" <> cur."Id" AND pg_temp.m97b_in_force(s."Status", s."IsActive", s."IsDeleted");
        RETURN;
    END IF;

    PERFORM pg_temp.m97b_cancel(s."Id", 'remplacé par ' || p_key)
       FROM "Subscriptions" s
      WHERE s."UserId" = p_user AND pg_temp.m97b_in_force(s."Status", s."IsActive", s."IsDeleted");

    INSERT INTO "Subscriptions" ("UserId", "PricingPlanId", "PlanVersionId", "PlanName", "Audience", "BillingPeriod",
                                 "Status", "IsActive", "IsDeleted", "StartDate", "EndDate", "CurrentPeriodStart",
                                 "AutoRenew", "CancelAtPeriodEnd", "PricePaidTtc", "RenewalCount",
                                 "TokensUsedThisMonth", "CreatedAt")
    VALUES (p_user, pl.plan_id, pl.version_id, pl.name, pl.audience, p_period,
            'active', TRUE, FALSE, NOW(), p_end, NOW(),
            p_auto, FALSE, 0, 0, 0, NOW())
    RETURNING "Id" INTO new_id;
    PERFORM pg_temp.m97b_audit('user.subscription_granted', 'user', p_user::TEXT, 'abonnement ' || new_id,
        NULL, format('%s, %s, fin %s, renouvellement %s (prix catalogue %s XAF TTC, non facturé)', p_key, p_period, p_end, p_auto, price));
END $fn$;

-- Ramène un compte au plan gratuit de son public.
CREATE OR REPLACE FUNCTION pg_temp.m97b_set_free(p_user INTEGER)
RETURNS VOID LANGUAGE plpgsql AS $fn$
BEGIN
    PERFORM pg_temp.m97b_cancel(s."Id", 'compte de référence en gratuit')
       FROM "Subscriptions" s
      WHERE s."UserId" = p_user AND pg_temp.m97b_in_force(s."Status", s."IsActive", s."IsDeleted");
END $fn$;

CREATE OR REPLACE FUNCTION pg_temp.m97b_role(p_user INTEGER) RETURNS TEXT LANGUAGE sql AS $fn$
    SELECT LOWER("Role") FROM "Users" WHERE "Id" = p_user;
$fn$;

-- Lien parent-enfant accepté (créé ou accepté au besoin).
CREATE OR REPLACE FUNCTION pg_temp.m97b_link(p_parent INTEGER, p_child INTEGER)
RETURNS VOID LANGUAGE plpgsql AS $fn$
DECLARE l RECORD;
BEGIN
    SELECT * INTO l FROM "ParentStudentLinks" WHERE "ParentId" = p_parent AND "StudentId" = p_child ORDER BY "Id" LIMIT 1;
    IF NOT FOUND THEN
        INSERT INTO "ParentStudentLinks" ("ParentId", "StudentId", "Status", "InitiatedBy", "CreatedAt", "UpdatedAt")
        VALUES (p_parent, p_child, 'accepted', p_parent, NOW(), NOW());
        PERFORM pg_temp.m97b_audit('user.updated', 'user', p_parent::TEXT, 'lien parent-enfant', NULL,
            format('enfant %s lié et accepté', p_child));
    ELSIF LOWER(l."Status") <> 'accepted' THEN
        UPDATE "ParentStudentLinks" SET "Status" = 'accepted', "UpdatedAt" = NOW() WHERE "Id" = l."Id";
        PERFORM pg_temp.m97b_audit('user.updated', 'user', p_parent::TEXT, 'lien parent-enfant', l."Status",
            format('enfant %s accepté', p_child));
    END IF;
END $fn$;

-- ── 1. Statuts hérités ───────────────────────────────────────────────────
DO $$
DECLARE s RECORD;
BEGIN
    FOR s IN SELECT * FROM "Subscriptions"
              WHERE "Status" IS NULL OR LOWER("Status") NOT IN ('active', 'trialing', 'past_due', 'cancelled', 'expired') LOOP
        UPDATE "Subscriptions" SET "Status" = 'cancelled', "IsActive" = FALSE, "UpdatedAt" = NOW() WHERE "Id" = s."Id";
        PERFORM pg_temp.m97b_audit('user.subscription_granted', 'user', s."UserId"::TEXT, 'abonnement ' || s."Id",
            'statut ' || COALESCE(s."Status", 'vide'), 'statut hérité inconnu : cancelled');
    END LOOP;
END $$;

-- ── 2. Correspondance grille A → nouvelle grille (étude 6.2 point 3) ─────
DO $$
DECLARE
    s RECORD; target TEXT; period TEXT; pl RECORD; user_audience TEXT;
BEGIN
    FOR s IN
        SELECT sub.*, old."Name" AS old_name, LOWER(old."Category") AS old_category, old."Period" AS old_period
          FROM "Subscriptions" sub
          JOIN "PricingPlans" old ON old."Id" = sub."PricingPlanId"
         WHERE old."PlanKey" IS NULL
           AND pg_temp.m97b_in_force(sub."Status", sub."IsActive", sub."IsDeleted")
         ORDER BY sub."Id"
    LOOP
        target := CASE
            WHEN s.old_name = 'Standard' AND s.old_category = 'students' THEN 'student-standard'
            WHEN s.old_name = 'Premium'  AND s.old_category = 'students' THEN 'student-premium'
            WHEN s.old_name = 'Annuel'   THEN 'student-ultime'
            WHEN s.old_name = 'Pro'      THEN 'teacher-pro'
            WHEN s.old_name = 'Expert'   THEN 'teacher-expert'
            WHEN s.old_name = 'Famille'  THEN 'parent-famille'
            WHEN s.old_name IN ('Famille+', 'VIP') THEN 'parent-famille-plus'
            ELSE NULL END;  -- Starter, Basique professeur (gratuits) ou plan inconnu
        IF target IS NULL THEN
            PERFORM pg_temp.m97b_cancel(s."Id", format('plan %s sans équivalent payant : compte en gratuit', s.old_name));
            CONTINUE;
        END IF;

        SELECT * INTO pl FROM pg_temp.m97b_plan(target);
        user_audience := CASE pg_temp.m97b_role(s."UserId")
            WHEN 'student' THEN 'student' WHEN 'parent' THEN 'parent' WHEN 'teacher' THEN 'teacher' ELSE NULL END;
        IF user_audience IS DISTINCT FROM pl.audience THEN
            PERFORM pg_temp.m97b_cancel(s."Id", format('plan %s destiné au public %s, compte %s',
                s.old_name, pl.audience, COALESCE(pg_temp.m97b_role(s."UserId"), 'inconnu')));
            CONTINUE;
        END IF;

        period := CASE WHEN s.old_name = 'Annuel' OR COALESCE(s.old_period, '') ILIKE '%an%' THEN 'yearly' ELSE 'monthly' END;
        UPDATE "Subscriptions"
           SET "PricingPlanId" = pl.plan_id, "PlanVersionId" = pl.version_id, "PlanName" = pl.name,
               "Audience" = pl.audience, "BillingPeriod" = period,
               "EndDate" = COALESCE("EndDate", NOW() + INTERVAL '1 month'),
               "CurrentPeriodStart" = COALESCE("CurrentPeriodStart", "StartDate"),
               "Status" = LOWER("Status"), "UpdatedAt" = NOW()
         WHERE "Id" = s."Id";
        PERFORM pg_temp.m97b_audit('user.subscription_granted', 'user', s."UserId"::TEXT, 'abonnement ' || s."Id",
            format('%s (%s), fin %s', s.old_name, s.old_category, s."EndDate"),
            format('%s, %s, fin %s', target, period, COALESCE(s."EndDate", NOW() + INTERVAL '1 month')));
    END LOOP;
END $$;

-- ── 3. Un seul abonnement en vigueur par compte : le plus récent reste ───
DO $$
BEGIN
    PERFORM pg_temp.m97b_cancel(d."Id", 'doublon : un seul abonnement en vigueur par compte')
       FROM (SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "UserId" ORDER BY "EndDate" DESC NULLS LAST, "Id" DESC) AS rn
               FROM "Subscriptions"
              WHERE pg_temp.m97b_in_force("Status", "IsActive", "IsDeleted")) d
      WHERE d.rn > 1;
END $$;

-- ── 4. Comptes de référence (étude 6.2 point 4) ──────────────────────────
DO $$
DECLARE
    inst INTEGER; lic INTEGER; promo INTEGER; seats INTEGER := 30;
BEGIN
    -- 21 : rôle organization → institution (décision 9.3).
    IF pg_temp.m97b_role(21) = 'organization' THEN
        UPDATE "Users" SET "Role" = 'institution' WHERE "Id" = 21;
        PERFORM pg_temp.m97b_audit('user.role_changed', 'user', '21', 'org@winplus.cm', 'organization', 'institution');
    END IF;

    -- 30 : élève Premium annuel, renouvellement par le solde ; enfant accepté de 26.
    IF pg_temp.m97b_role(30) = 'student' THEN
        PERFORM pg_temp.m97b_set_plan(30, 'student-premium', 'yearly', NOW() + INTERVAL '1 year', TRUE);
        IF pg_temp.m97b_role(26) = 'parent' THEN PERFORM pg_temp.m97b_link(26, 30); END IF;
    ELSE RAISE NOTICE 'Compte 30 : rôle % inattendu (student attendu), laissé tel quel.', pg_temp.m97b_role(30); END IF;

    -- 26 : parent Famille mensuel, renouvellement par le solde.
    IF pg_temp.m97b_role(26) = 'parent' THEN
        PERFORM pg_temp.m97b_set_plan(26, 'parent-famille', 'monthly', NOW() + INTERVAL '1 month', TRUE);
    ELSE RAISE NOTICE 'Compte 26 : rôle % inattendu (parent attendu), laissé tel quel.', pg_temp.m97b_role(26); END IF;

    -- 50 : professeur Pro trimestriel (75 %).
    IF pg_temp.m97b_role(50) = 'teacher' THEN
        PERFORM pg_temp.m97b_set_plan(50, 'teacher-pro', 'quarterly', NOW() + INTERVAL '3 months', FALSE);
    ELSE RAISE NOTICE 'Compte 50 : rôle % inattendu (teacher attendu), laissé tel quel.', pg_temp.m97b_role(50); END IF;

    -- 19 : élève Standard mensuel arrivant à échéance à J+2, renouvellement
    -- activé (le solde nul fera échouer le débit : grâce de 3 jours, coupure).
    IF pg_temp.m97b_role(19) = 'student' THEN
        PERFORM pg_temp.m97b_set_plan(19, 'student-standard', 'monthly', NOW() + INTERVAL '2 days', TRUE);
    ELSE RAISE NOTICE 'Compte 19 : rôle % inattendu (student attendu), laissé tel quel.', pg_temp.m97b_role(19); END IF;

    -- 40, 41, 42 : gratuits.
    PERFORM pg_temp.m97b_set_free(40);
    PERFORM pg_temp.m97b_set_free(41);
    PERFORM pg_temp.m97b_set_free(42);

    -- 42 : un enfant en suivi de base (40, compte élève de test, s'il n'en a aucun).
    IF pg_temp.m97b_role(42) = 'parent' AND pg_temp.m97b_role(40) = 'student'
       AND NOT EXISTS (SELECT 1 FROM "ParentStudentLinks" WHERE "ParentId" = 42 AND LOWER("Status") = 'accepted') THEN
        PERFORM pg_temp.m97b_link(42, 40);
    END IF;

    -- 43 : établissement ; 40 rattaché et accepté ; licence d'essai de 30
    -- jours, 30 sièges, ouverte par une promotion.
    SELECT COALESCE(u."InstitutionId", (SELECT i."Id" FROM "Institutions" i WHERE i."WalletOwnerUserId" = 43 AND NOT i."IsDeleted" ORDER BY i."Id" LIMIT 1))
      INTO inst FROM "Users" u WHERE u."Id" = 43;
    IF inst IS NULL THEN
        RAISE NOTICE 'Compte 43 : aucun établissement rattaché, licence d''essai non créée.';
    ELSE
        IF pg_temp.m97b_role(40) = 'student' THEN
            IF NOT EXISTS (SELECT 1 FROM "InstitutionStudents" WHERE "InstitutionId" = inst AND "StudentId" = 40) THEN
                INSERT INTO "InstitutionStudents" ("InstitutionId", "StudentId", "IsActive", "ConfirmationStatus", "RespondedAt", "CreatedAt")
                VALUES (inst, 40, TRUE, 'accepted', NOW(), NOW());
                PERFORM pg_temp.m97b_audit('user.updated', 'user', '40', 'rattachement établissement', NULL,
                    format('rattaché et accepté à l''établissement %s', inst));
            ELSIF EXISTS (SELECT 1 FROM "InstitutionStudents" WHERE "InstitutionId" = inst AND "StudentId" = 40
                           AND ("IsActive" = FALSE OR "ConfirmationStatus" <> 'accepted')) THEN
                UPDATE "InstitutionStudents" SET "IsActive" = TRUE, "ConfirmationStatus" = 'accepted', "RespondedAt" = NOW()
                 WHERE "InstitutionId" = inst AND "StudentId" = 40;
                PERFORM pg_temp.m97b_audit('user.updated', 'user', '40', 'rattachement établissement', 'en attente ou inactif',
                    format('accepté à l''établissement %s', inst));
            END IF;
        END IF;

        IF NOT EXISTS (SELECT 1 FROM "InstitutionLicenses" WHERE "InstitutionId" = inst
                        AND "Status" IN ('trialing', 'active', 'past_due')) THEN
            SELECT "Id" INTO promo FROM "PromoCodes" WHERE "Code" = 'ESSAI-ETAB-97B';
            IF promo IS NULL THEN
                INSERT INTO "PromoCodes" ("Code", "Description", "DiscountType", "DiscountValue", "UsageLimit", "UsageCount",
                                          "PerUserLimit", "ValidFrom", "ValidUntil", "IsActive", "AppliesTo", "Kind",
                                          "RequiresCode", "SubscriptionDuration", "TrialDays", "Eligibility",
                                          "EligibleInstitutionIds", "Stackable", "CreatedBy", "CreatedAt")
                VALUES ('ESSAI-ETAB-97B', 'Essai de licence établissement de 30 jours (migration 9.7b, compte de test 43)',
                        'Percentage', 0, 1, 0, 1, NOW(), NOW() + INTERVAL '30 days', TRUE, 'institution_license', 'trial_days',
                        TRUE, 'first_payment', 30, 'listed', inst::TEXT, FALSE,
                        COALESCE((SELECT "Id" FROM "Users" WHERE "Id" = 17), (SELECT MIN("Id") FROM "Users" WHERE LOWER("Role") = 'admin')),
                        NOW())
                RETURNING "Id" INTO promo;
                PERFORM pg_temp.m97b_audit('promo.created', 'promo', promo::TEXT, 'ESSAI-ETAB-97B', NULL, 'essai 30 jours, établissement ' || inst);
            END IF;

            INSERT INTO "InstitutionLicenses" ("InstitutionId", "Tier", "Status", "Term", "StartDate", "EndDate",
                                               "StudentSeats", "TeacherSeats", "PricePerSeatTtc", "TotalPriceTtc",
                                               "IsNegotiated", "AutoRenew", "CancelAtPeriodEnd", "AiBudgetTokens",
                                               "PromoCodeId", "Notes", "CreatedAt")
            VALUES (inst, 'trial', 'trialing', 'year', NOW(), NOW() + INTERVAL '30 days',
                    seats, GREATEST(3, CEIL(seats / 15.0)::INTEGER), 0, 0,
                    FALSE, FALSE, FALSE, 0, promo, 'Essai ouvert par la migration 9.7b', NOW())
            RETURNING "Id" INTO lic;
            INSERT INTO "PromoCodeUsages" ("PromoCodeId", "UserId", "OrderId", "SubscriptionId", "Target", "DiscountAmount", "UsedAt")
            VALUES (promo, 43, NULL, NULL, 'institution_license', 0, NOW());
            UPDATE "PromoCodes" SET "UsageCount" = "UsageCount" + 1 WHERE "Id" = promo;
            IF EXISTS (SELECT 1 FROM "InstitutionStudents" WHERE "InstitutionId" = inst AND "StudentId" = 40
                        AND "IsActive" AND "ConfirmationStatus" = 'accepted') THEN
                INSERT INTO "InstitutionSeats" ("LicenseId", "Kind", "UserId", "AssignedAt", "AssignedByUserId")
                VALUES (lic, 'student', 40, NOW(), 43);
            END IF;
            PERFORM pg_temp.m97b_audit('license.created', 'institution', inst::TEXT, 'licence ' || lic, NULL,
                format('essai 30 jours, %s sièges élèves, code ESSAI-ETAB-97B', seats));
        END IF;
    END IF;
END $$;

-- ── 5. Index et contrainte ───────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'UX_Subscriptions_OneInForcePerUser') THEN
        CREATE UNIQUE INDEX "UX_Subscriptions_OneInForcePerUser" ON "Subscriptions"("UserId")
            WHERE "IsDeleted" = FALSE AND "IsActive" = TRUE AND LOWER("Status") IN ('active', 'trialing', 'past_due');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_Subscriptions_Status') THEN
        ALTER TABLE "Subscriptions" ADD CONSTRAINT "CK_Subscriptions_Status"
            CHECK (LOWER("Status") IN ('active', 'trialing', 'past_due', 'cancelled', 'expired'));
    END IF;
END $$;

-- ── 6. Vérifications ─────────────────────────────────────────────────────
DO $$
DECLARE b NUMERIC; u INTEGER;
BEGIN
    FOREACH u IN ARRAY ARRAY[19, 30, 26] LOOP
        SELECT COALESCE(SUM("Amount"), 0) INTO b FROM "WalletTransactions"
         WHERE "OwnerId" = u AND "OwnerType" <> 'platform' AND "Status" = 'confirmed';
        IF u = 19 AND b <> 0 THEN
            RAISE WARNING 'Compte 19 : solde de % XAF, NON NUL. Le test d''échec de renouvellement suppose un solde nul : à corriger à la main.', b;
        ELSE
            RAISE NOTICE 'Compte % : solde disponible % XAF.', u, b;
        END IF;
    END LOOP;
    RAISE NOTICE 'Abonnements en vigueur restant sur un ancien plan : %',
        (SELECT COUNT(*) FROM "Subscriptions" s JOIN "PricingPlans" p ON p."Id" = s."PricingPlanId"
          WHERE p."PlanKey" IS NULL AND pg_temp.m97b_in_force(s."Status", s."IsActive", s."IsDeleted"));
END $$;

SELECT s."UserId", p."PlanKey", s."BillingPeriod", s."Status", s."EndDate", s."AutoRenew"
  FROM "Subscriptions" s JOIN "PricingPlans" p ON p."Id" = s."PricingPlanId"
 WHERE s."UserId" IN (19, 26, 30, 40, 41, 42, 43, 50)
   AND pg_temp.m97b_in_force(s."Status", s."IsActive", s."IsDeleted")
 ORDER BY s."UserId";

COMMIT;
