-- ════════════════════════════════════════════════════════════════════════════
-- scripts/9.7b/setup_test_institution_43.sql : compte de test 43 (institution)
-- Étude ETUDE_9_7B_ABONNEMENTS.md section 6.2 point 4 (état cible du compte 43 :
-- licence d'essai de 30 jours et 30 sièges ouverte par une promotion ; élève
-- 40 rattaché et accepté).
--
-- Pourquoi : à la migration 9.7b (migrate_subscriptions.sql), le compte 43
-- n'était rattaché à aucun établissement ; sa licence d'essai n'a donc pas été
-- créée (avis « Compte 43 : aucun établissement rattaché »).
--
-- SCRIPT MANUEL ET IDEMPOTENT, hors dotnet/Migrations. JAMAIS en production
-- sans décision explicite : il crée des données de TEST (établissement de
-- test, promotion ESSAI-ETAB-97B réservée à cet établissement). À lancer
-- APRÈS le déploiement du code 9.7b, après les scripts SQL_* et après
-- migrate_subscriptions.sql, sur une base sauvegardée :
--   psql -U <user> -d <base> -v ON_ERROR_STOP=1 -f scripts/9.7b/setup_test_institution_43.sql
--
-- Ce que fait le script (chaque étape seulement si l'état cible manque) :
--   1. établissement de test : celui du compte 43 s'il en a un, sinon celui
--      dont 43 tient le portefeuille, sinon celui nommé « Établissement de
--      test WinPlus (compte 43) », sinon il est créé ; 43 y est rattaché
--      (Users.InstitutionId) et en devient le titulaire du portefeuille s'il
--      n'en a pas ;
--   2. élève de test 40 rattaché à cet établissement, rattachement ACCEPTÉ
--      (IsActive, ConfirmationStatus = accepted, RespondedAt) ;
--   3. promotion ESSAI-ETAB-97B (essai de 30 jours, licences, réservée à cet
--      établissement, une utilisation) créée si absente, ou ouverte à cet
--      établissement si elle existe déjà sans avoir servi ;
--   4. licence d'essai ouverte EXACTEMENT comme InstitutionLicenseService
--      .StartTrialAsync (même contrôle « une seule fois par établissement »,
--      mêmes règles d'offre que PromotionService.CheckAsync, mêmes valeurs :
--      tier trial, terme quarter, statut trialing, fin = début + TrialDays,
--      30 sièges élèves, max(3, ceil(30 / 15)) = 3 sièges enseignants, prix 0,
--      budget WinAI 20 000 × 30 × jours / 330 tokens, CreatedByUserId = 43),
--      utilisation de la promotion inscrite comme RecordUsageAsync
--      (PromoCodeUsages, cible institution_license, remise 0, UsageCount + 1),
--      puis sièges attribués comme AutoAssignAllAsync (élèves acceptés par date
--      de réponse, puis enseignants affiliés acceptés, dans la limite des
--      sièges, AssignedByUserId nul) ;
--   5. siège de l'élève 40 si une licence est en cours et qu'il n'en a pas
--      (comme TryAutoAssignAsync à l'acceptation du rattachement).
-- Chaque modification est inscrite dans AdminAuditLogs (motif « compte de
-- test 43, script manuel 9.7b »). Une seule transaction : en cas d'erreur,
-- rien n'est appliqué. Rejoué, il ne change rien et n'écrit aucun journal.
-- Le cache des droits du serveur (60 s) se met à jour de lui-même.
-- ════════════════════════════════════════════════════════════════════════════

\set ON_ERROR_STOP on
BEGIN;

CREATE OR REPLACE FUNCTION pg_temp.t43_audit(p_action TEXT, p_type TEXT, p_id TEXT, p_label TEXT, p_before TEXT, p_after TEXT)
RETURNS VOID LANGUAGE sql AS $fn$
    INSERT INTO "AdminAuditLogs" ("ActorUserId", "ActorEmail", "Action", "TargetType", "TargetId", "TargetLabel",
                                  "Before", "After", "Reason", "IpAddress", "CreatedAt")
    VALUES (NULL, 'script-9.7b-compte-43', p_action, p_type, p_id, p_label, p_before, p_after,
            'compte de test 43, script manuel 9.7b', NULL, NOW());
$fn$;

DO $$
DECLARE
    inst     INTEGER;
    promo    INTEGER;
    lic      INTEGER;
    days     INTEGER;
    seats    CONSTANT INTEGER := 30;           -- LicensePolicy.TrialSeats
    tseats   INTEGER;
    p        RECORD;
    s        RECORD;
    used     INTEGER;
    cur_lic  INTEGER;
    cur_cap  INTEGER;
    now_ts   TIMESTAMPTZ := NOW();
BEGIN
    -- ── Prérequis ────────────────────────────────────────────────────────
    IF to_regclass('"InstitutionLicenses"') IS NULL OR to_regclass('"InstitutionSeats"') IS NULL THEN
        RAISE EXCEPTION 'Tables des licences absentes : lancer SQL_Institution97bLicenses.sql avant ce script.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'PromoCodes' AND column_name = 'TrialDays') THEN
        RAISE EXCEPTION 'Colonnes de promotion 9.7b absentes : lancer SQL_Subscriptions97bCycle.sql avant ce script.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id" = 43 AND LOWER("Role") = 'institution') THEN
        RAISE EXCEPTION 'Le compte 43 est absent ou n''a pas le rôle institution : script arrêté.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id" = 40 AND LOWER("Role") = 'student') THEN
        RAISE EXCEPTION 'Le compte 40 est absent ou n''a pas le rôle student : script arrêté.';
    END IF;

    -- ── 1. Établissement de test et rattachement du compte 43 ────────────
    SELECT u."InstitutionId" INTO inst FROM "Users" u
      JOIN "Institutions" i ON i."Id" = u."InstitutionId" AND NOT i."IsDeleted"
     WHERE u."Id" = 43;
    IF inst IS NULL THEN
        SELECT i."Id" INTO inst FROM "Institutions" i
         WHERE i."WalletOwnerUserId" = 43 AND NOT i."IsDeleted" ORDER BY i."Id" LIMIT 1;
    END IF;
    IF inst IS NULL THEN
        SELECT i."Id" INTO inst FROM "Institutions" i
         WHERE i."Name" = 'Établissement de test WinPlus (compte 43)' AND NOT i."IsDeleted" ORDER BY i."Id" LIMIT 1;
    END IF;
    IF inst IS NULL THEN
        INSERT INTO "Institutions" ("Name", "Code", "Country", "City", "Email", "Type", "IsActive",
                                    "WalletOwnerUserId", "CreatedAt", "IsDeleted")
        VALUES ('Établissement de test WinPlus (compte 43)', 'TEST-43', 'CM', 'Douala',
                (SELECT "Email" FROM "Users" WHERE "Id" = 43), 'school', TRUE, 43, now_ts, FALSE)
        RETURNING "Id" INTO inst;
        PERFORM pg_temp.t43_audit('institution.created', 'institution', inst::TEXT,
            'Établissement de test WinPlus (compte 43)', NULL, 'établissement de test créé, portefeuille tenu par 43');
    END IF;

    IF EXISTS (SELECT 1 FROM "Users" WHERE "Id" = 43 AND "InstitutionId" IS DISTINCT FROM inst) THEN
        PERFORM pg_temp.t43_audit('user.updated', 'user', '43', 'rattachement établissement',
            (SELECT COALESCE("InstitutionId"::TEXT, 'aucun') FROM "Users" WHERE "Id" = 43), format('établissement %s', inst));
        UPDATE "Users" SET "InstitutionId" = inst, "UpdatedAt" = now_ts WHERE "Id" = 43;
    END IF;
    IF EXISTS (SELECT 1 FROM "Institutions" WHERE "Id" = inst AND "WalletOwnerUserId" IS NULL) THEN
        UPDATE "Institutions" SET "WalletOwnerUserId" = 43, "UpdatedAt" = now_ts WHERE "Id" = inst;
        PERFORM pg_temp.t43_audit('institution.updated', 'institution', inst::TEXT, 'titulaire du portefeuille', 'aucun', '43');
    END IF;

    -- ── 2. Élève 40 rattaché et accepté ──────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM "InstitutionStudents" WHERE "InstitutionId" = inst AND "StudentId" = 40) THEN
        INSERT INTO "InstitutionStudents" ("InstitutionId", "StudentId", "IsActive", "ConfirmationStatus", "RespondedAt", "CreatedAt")
        VALUES (inst, 40, TRUE, 'accepted', now_ts, now_ts);
        PERFORM pg_temp.t43_audit('user.updated', 'user', '40', 'rattachement établissement', NULL,
            format('rattaché et accepté à l''établissement %s', inst));
    ELSIF EXISTS (SELECT 1 FROM "InstitutionStudents" WHERE "InstitutionId" = inst AND "StudentId" = 40
                   AND ("IsActive" = FALSE OR "ConfirmationStatus" <> 'accepted')) THEN
        UPDATE "InstitutionStudents" SET "IsActive" = TRUE, "ConfirmationStatus" = 'accepted', "RespondedAt" = now_ts
         WHERE "InstitutionId" = inst AND "StudentId" = 40;
        PERFORM pg_temp.t43_audit('user.updated', 'user', '40', 'rattachement établissement', 'en attente ou inactif',
            format('accepté à l''établissement %s', inst));
    END IF;

    -- ── 3 et 4. Licence d'essai (une seule fois par établissement) ───────
    -- Même garde que StartTrialAsync : aucune licence, quel que soit son statut.
    IF NOT EXISTS (SELECT 1 FROM "InstitutionLicenses" WHERE "InstitutionId" = inst) THEN
        SELECT "Id" INTO promo FROM "PromoCodes" WHERE "Code" = 'ESSAI-ETAB-97B';
        IF promo IS NULL THEN
            INSERT INTO "PromoCodes" ("Code", "Description", "DiscountType", "DiscountValue", "UsageLimit", "UsageCount",
                                      "PerUserLimit", "ValidFrom", "ValidUntil", "IsActive", "AppliesTo", "Kind",
                                      "RequiresCode", "SubscriptionDuration", "TrialDays", "Eligibility",
                                      "EligibleInstitutionIds", "Stackable", "CreatedBy", "CreatedAt")
            VALUES ('ESSAI-ETAB-97B', 'Essai de licence établissement de 30 jours (compte de test 43, script manuel 9.7b)',
                    'Percentage', 0, 1, 0, 1, now_ts, now_ts + INTERVAL '30 days', TRUE, 'institution_license', 'trial_days',
                    TRUE, 'first_payment', 30, 'listed', inst::TEXT, FALSE,
                    COALESCE((SELECT "Id" FROM "Users" WHERE "Id" = 17), (SELECT MIN("Id") FROM "Users" WHERE LOWER("Role") = 'admin')),
                    now_ts)
            RETURNING "Id" INTO promo;
            PERFORM pg_temp.t43_audit('promo.created', 'promo', promo::TEXT, 'ESSAI-ETAB-97B', NULL,
                'essai 30 jours, établissement ' || inst);
        ELSIF NOT EXISTS (SELECT 1 FROM "PromoCodeUsages" WHERE "PromoCodeId" = promo)
              AND NOT (inst::TEXT = ANY (string_to_array(REPLACE(COALESCE((SELECT "EligibleInstitutionIds" FROM "PromoCodes" WHERE "Id" = promo), ''), ' ', ''), ','))) THEN
            -- Promotion créée ailleurs mais jamais utilisée : ouverte à cet établissement.
            UPDATE "PromoCodes"
               SET "EligibleInstitutionIds" = CASE WHEN COALESCE("EligibleInstitutionIds", '') = '' THEN inst::TEXT
                                                   ELSE "EligibleInstitutionIds" || ',' || inst::TEXT END,
                   "Eligibility" = 'listed', "UpdatedAt" = now_ts
             WHERE "Id" = promo;
            PERFORM pg_temp.t43_audit('promo.updated', 'promo', promo::TEXT, 'ESSAI-ETAB-97B', NULL,
                'ouverte à l''établissement ' || inst);
        END IF;

        -- Règles de PromotionService.CheckAsync pour une licence d'essai.
        SELECT * INTO p FROM "PromoCodes" WHERE "Id" = promo;
        SELECT COUNT(*) INTO used FROM "PromoCodeUsages" WHERE "PromoCodeId" = promo AND "UserId" = 43;
        IF NOT p."IsActive" THEN RAISE EXCEPTION 'ESSAI-ETAB-97B : offre inactive.'; END IF;
        IF p."ValidFrom" > now_ts THEN RAISE EXCEPTION 'ESSAI-ETAB-97B : offre pas encore valable.'; END IF;
        IF p."ValidUntil" IS NOT NULL AND p."ValidUntil" < now_ts THEN
            RAISE EXCEPTION 'ESSAI-ETAB-97B : offre expirée (%). Prolonger sa validité depuis l''écran admin des promotions, puis relancer.', p."ValidUntil";
        END IF;
        IF NOT (string_to_array(REPLACE(LOWER(COALESCE(p."AppliesTo", '')), ' ', ''), ',') && ARRAY['institution_license', 'all']) THEN
            RAISE EXCEPTION 'ESSAI-ETAB-97B : l''offre ne vise pas les licences (AppliesTo = %).', p."AppliesTo";
        END IF;
        IF p."Kind" <> 'trial_days' THEN RAISE EXCEPTION 'ESSAI-ETAB-97B : l''offre n''est pas un essai (Kind = %).', p."Kind"; END IF;
        IF p."UsageLimit" IS NOT NULL AND p."UsageCount" >= p."UsageLimit" THEN
            RAISE EXCEPTION 'ESSAI-ETAB-97B : limite d''utilisation atteinte.';
        END IF;
        IF p."PerUserLimit" IS NOT NULL AND used >= p."PerUserLimit" THEN
            RAISE EXCEPTION 'ESSAI-ETAB-97B : déjà utilisée par le compte 43.';
        END IF;
        IF COALESCE(p."TargetRoles", '') <> ''
           AND NOT ('institution' = ANY (string_to_array(REPLACE(LOWER(p."TargetRoles"), ' ', ''), ','))) THEN
            RAISE EXCEPTION 'ESSAI-ETAB-97B : l''offre ne vise pas les comptes institution.';
        END IF;
        IF p."Eligibility" = 'listed'
           AND NOT ('43' = ANY (string_to_array(REPLACE(COALESCE(p."EligibleUserIds", ''), ' ', ''), ','))
                    OR inst::TEXT = ANY (string_to_array(REPLACE(COALESCE(p."EligibleInstitutionIds", ''), ' ', ''), ','))) THEN
            RAISE EXCEPTION 'ESSAI-ETAB-97B : l''offre n''est pas destinée à l''établissement %.', inst;
        END IF;

        days := GREATEST(1, COALESCE(p."TrialDays", 7));     -- Build : Math.Max(1, TrialDays ?? 7)
        tseats := GREATEST(3, CEIL(seats / 15.0)::INTEGER);   -- LicensePolicy.TeacherSeatsFor(30)
        INSERT INTO "InstitutionLicenses" ("InstitutionId", "Tier", "Status", "Term", "StartDate", "EndDate", "GraceUntil",
                                           "StudentSeats", "TeacherSeats", "PricePerSeatTtc", "TotalPriceTtc",
                                           "IsNegotiated", "AutoRenew", "CancelAtPeriodEnd", "AiBudgetTokens",
                                           "PromoCodeId", "CreatedByUserId", "Notes", "CreatedAt")
        VALUES (inst, 'trial', 'trialing', 'quarter', now_ts, now_ts + make_interval(days => days), NULL,
                seats, tseats, 0, 0,
                FALSE, FALSE, FALSE, (20000::BIGINT * seats * days) / 330,
                promo, 43, NULL, now_ts)
        RETURNING "Id" INTO lic;

        -- RecordUsageAsync(promo, 43, null, null, institution_license, 0).
        INSERT INTO "PromoCodeUsages" ("PromoCodeId", "UserId", "OrderId", "SubscriptionId", "Target", "DiscountAmount", "UsedAt")
        VALUES (promo, 43, NULL, NULL, 'institution_license', 0, now_ts);
        UPDATE "PromoCodes" SET "UsageCount" = "UsageCount" + 1 WHERE "Id" = promo;

        -- AutoAssignAllAsync : élèves acceptés, puis enseignants affiliés.
        FOR s IN SELECT "StudentId" AS uid FROM "InstitutionStudents"
                  WHERE "InstitutionId" = inst AND "IsActive" AND "ConfirmationStatus" = 'accepted'
                  ORDER BY COALESCE("RespondedAt", "CreatedAt"), "Id" LOOP
            EXIT WHEN (SELECT COUNT(*) FROM "InstitutionSeats" WHERE "LicenseId" = lic AND "Kind" = 'student' AND "ReleasedAt" IS NULL) >= seats;
            IF NOT EXISTS (SELECT 1 FROM "InstitutionSeats" WHERE "LicenseId" = lic AND "Kind" = 'student' AND "UserId" = s.uid AND "ReleasedAt" IS NULL) THEN
                INSERT INTO "InstitutionSeats" ("LicenseId", "Kind", "UserId", "AssignedAt", "ReleasedAt", "AssignedByUserId")
                VALUES (lic, 'student', s.uid, now_ts, NULL, NULL);
            END IF;
        END LOOP;
        FOR s IN SELECT "TeacherId" AS uid FROM "InstitutionTeacherLinks"
                  WHERE "InstitutionId" = inst AND "Status" = 'accepted'
                  ORDER BY "UpdatedAt", "Id" LOOP
            EXIT WHEN (SELECT COUNT(*) FROM "InstitutionSeats" WHERE "LicenseId" = lic AND "Kind" = 'teacher' AND "ReleasedAt" IS NULL) >= tseats;
            IF NOT EXISTS (SELECT 1 FROM "InstitutionSeats" WHERE "LicenseId" = lic AND "Kind" = 'teacher' AND "UserId" = s.uid AND "ReleasedAt" IS NULL) THEN
                INSERT INTO "InstitutionSeats" ("LicenseId", "Kind", "UserId", "AssignedAt", "ReleasedAt", "AssignedByUserId")
                VALUES (lic, 'teacher', s.uid, now_ts, NULL, NULL);
            END IF;
        END LOOP;

        PERFORM pg_temp.t43_audit('license.created', 'institution', inst::TEXT, 'licence ' || lic, NULL,
            format('essai %s jours, %s sièges élèves, %s sièges enseignants, code ESSAI-ETAB-97B', days, seats, tseats));
        RAISE NOTICE 'Compte 43 : licence d''essai % ouverte pour l''établissement % jusqu''au %.', lic, inst,
            to_char(now_ts + make_interval(days => days), 'DD/MM/YYYY');
    END IF;

    -- ── 5. Siège de l'élève 40 sur la licence en cours ───────────────────
    SELECT l."Id", l."StudentSeats" INTO cur_lic, cur_cap FROM "InstitutionLicenses" l
     WHERE l."InstitutionId" = inst
       AND ((l."Status" IN ('active', 'trialing') AND l."StartDate" <= now_ts AND l."EndDate" > now_ts)
            OR (l."Status" = 'past_due' AND l."GraceUntil" > now_ts))
     ORDER BY l."EndDate" DESC LIMIT 1;
    IF cur_lic IS NOT NULL
       AND EXISTS (SELECT 1 FROM "InstitutionStudents" WHERE "InstitutionId" = inst AND "StudentId" = 40
                    AND "IsActive" AND "ConfirmationStatus" = 'accepted')
       AND NOT EXISTS (SELECT 1 FROM "InstitutionSeats" WHERE "LicenseId" = cur_lic AND "Kind" = 'student' AND "UserId" = 40 AND "ReleasedAt" IS NULL)
       AND (SELECT COUNT(*) FROM "InstitutionSeats" WHERE "LicenseId" = cur_lic AND "Kind" = 'student' AND "ReleasedAt" IS NULL) < cur_cap THEN
        INSERT INTO "InstitutionSeats" ("LicenseId", "Kind", "UserId", "AssignedAt", "ReleasedAt", "AssignedByUserId")
        VALUES (cur_lic, 'student', 40, now_ts, NULL, NULL);
        PERFORM pg_temp.t43_audit('license.seat_assigned', 'institution', inst::TEXT, 'licence ' || cur_lic, NULL, 'siège élève attribué au compte 40');
    END IF;

    RAISE NOTICE 'Compte 43 : établissement %, élève 40 accepté, licences : %.', inst,
        (SELECT string_agg(format('%s %s→%s (%s sièges, %s attribués)', l."Status", to_char(l."StartDate", 'DD/MM'), to_char(l."EndDate", 'DD/MM/YYYY'),
                l."StudentSeats", (SELECT COUNT(*) FROM "InstitutionSeats" x WHERE x."LicenseId" = l."Id" AND x."ReleasedAt" IS NULL)), ', ')
           FROM "InstitutionLicenses" l WHERE l."InstitutionId" = inst);
END $$;

COMMIT;
