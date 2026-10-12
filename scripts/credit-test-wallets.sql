-- =====================================================================
-- Crédits de test des portefeuilles (suivi 17.13 / 17.15, décision du 2026-10-12)
--
--   élève      Id 30 : 35 000 XAF
--   parent     Id 26 : 75 000 XAF (part permanente du portefeuille parent)
--   professeur Id 50 : 85 000 XAF (portefeuille professeur / tuteur)
--
-- SCRIPT MANUEL, À LANCER UNE FOIS PAR L'ADMINISTRATEUR. Il est volontairement
-- hors de dotnet/Migrations/ : il ne doit jamais être rejoué automatiquement.
--
-- Chaque crédit reproduit exactement ce que laisse en base une vraie recharge
-- NotchPay réussie (WalletTopUpService.StartAsync, puis le webhook :
-- PaymentService -> commande « completed » -> WalletService.SyncWalletTopUpAsync) :
--   1. "Orders"            : commande porteuse sans ligne de contenu, statut
--                            « completed », Notes « Recharge du portefeuille WinPlus » ;
--   2. "WalletTopUps"      : intention de recharge liée à la commande, « completed » ;
--   3. "Payments"          : paiement « notchpay » « completed » de la commande ;
--   4. "WalletTransactions": UNE écriture EntryType « Recharge », confirmée,
--                            SourceType « WalletTopUp », SourceId = id de la recharge,
--                            IdempotencyKey « recharge:topup:{id} » (WalletService.TopUpKey),
--                            Description « Recharge Mobile Money », OwnerType déduit du
--                            rôle comme WalletOwnerTypes.ForRole (teacher -> teacher,
--                            parent -> parent, institution -> institution, autre -> user).
-- Une recharge parent n'a pas d'échéance (ExpiresAt NULL) : c'est la part
-- permanente du portefeuille parent (la dotation mensuelle, elle, est une
-- écriture MonthlyAllocation expirable, non utilisée ici).
--
-- Idempotence : chaque crédit porte une référence fixe et unique
-- (TEST-CREDIT-2026-10-12-U30 / -P26 / -T50), utilisée comme numéro de
-- commande (index unique), identifiant de requête client de la recharge et
-- référence du paiement ; chaque ligne est vérifiée avant insertion. Rejouer le
-- script ne crée rien de plus. Tout est fait dans une seule transaction : un
-- compte absent, inactif ou d'un rôle inattendu annule l'ensemble.
--
-- Exécution (base de production, par l'administrateur uniquement) :
--   psql -v ON_ERROR_STOP=1 -h <hôte> -U <utilisateur> -d <base> -f credit-test-wallets.sql
-- Le SELECT final affiche le solde disponible de chaque compte (somme des
-- écritures confirmées, même calcul que WalletService.GetAvailableAsync).
-- =====================================================================

SET client_encoding = 'UTF8';

BEGIN;

DO $$
DECLARE
    c            RECORD;
    v_role       TEXT;
    v_active     BOOLEAN;
    v_deleted    BOOLEAN;
    v_owner_type TEXT;
    v_order_id   INTEGER;
    v_order_user INTEGER;
    v_topup_id   INTEGER;
    v_key        TEXT;
    v_now        TIMESTAMPTZ := now();
BEGIN
    FOR c IN
        SELECT * FROM (VALUES
            (30, 'student', 35000::NUMERIC, 'TEST-CREDIT-2026-10-12-U30'),
            (26, 'parent',  75000::NUMERIC, 'TEST-CREDIT-2026-10-12-P26'),
            (50, 'teacher', 85000::NUMERIC, 'TEST-CREDIT-2026-10-12-T50')
        ) AS t(user_id, expected_role, amount, ref)
    LOOP
        -- Compte : existant, actif, non supprimé, du rôle attendu.
        SELECT lower(trim(u."Role")), u."IsActive", u."IsDeleted"
          INTO v_role, v_active, v_deleted
          FROM "Users" u WHERE u."Id" = c.user_id;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Crédit %: compte % introuvable', c.ref, c.user_id;
        END IF;
        IF v_role IS DISTINCT FROM c.expected_role THEN
            RAISE EXCEPTION 'Crédit %: le compte % a le rôle « % », « % » attendu', c.ref, c.user_id, v_role, c.expected_role;
        END IF;
        IF NOT v_active OR v_deleted THEN
            RAISE EXCEPTION 'Crédit %: le compte % est inactif ou supprimé', c.ref, c.user_id;
        END IF;

        -- WalletOwnerTypes.ForRole
        v_owner_type := CASE v_role
            WHEN 'teacher' THEN 'teacher'
            WHEN 'parent' THEN 'parent'
            WHEN 'institution' THEN 'institution'
            ELSE 'user'
        END;

        -- 1. Commande porteuse (WalletTopUpService.StartAsync, puis « completed » au webhook).
        SELECT o."Id", o."UserId" INTO v_order_id, v_order_user FROM "Orders" o WHERE o."OrderNumber" = c.ref;
        IF v_order_id IS NULL THEN
            INSERT INTO "Orders" ("UserId", "OrderNumber", "TotalAmount", "Status", "PaymentMethod",
                                  "CreatedAt", "OrderDate", "CompletedDate", "UpdatedAt", "Notes",
                                  "DiscountAmount", "IsDeleted")
            VALUES (c.user_id, c.ref, c.amount, 'completed', 'mtn',
                    v_now, v_now, v_now, v_now, 'Recharge du portefeuille WinPlus',
                    0, FALSE)
            RETURNING "Id" INTO v_order_id;
        ELSIF v_order_user IS DISTINCT FROM c.user_id THEN
            RAISE EXCEPTION 'Crédit %: la commande % existe déjà pour un autre compte (%)', c.ref, v_order_id, v_order_user;
        END IF;

        -- 2. Intention de recharge (une par commande, index unique sur OrderId).
        SELECT w."Id" INTO v_topup_id FROM "WalletTopUps" w WHERE w."OrderId" = v_order_id;
        IF v_topup_id IS NULL THEN
            INSERT INTO "WalletTopUps" ("UserId", "OrderId", "AmountXaf", "Status", "ClientRequestId", "CreatedAt", "CompletedAt")
            VALUES (c.user_id, v_order_id, c.amount, 'completed', c.ref, v_now, v_now)
            RETURNING "Id" INTO v_topup_id;
        END IF;

        -- 3. Paiement NotchPay confirmé de la commande.
        IF NOT EXISTS (SELECT 1 FROM "Payments" p WHERE p."OrderId" = v_order_id) THEN
            INSERT INTO "Payments" ("OrderId", "UserId", "Amount", "Currency", "Status", "PaymentMethod",
                                    "NotchpayReference", "Operator", "Description",
                                    "InitiatedAt", "ProcessedAt", "CompletedAt", "RetryCount",
                                    "Metadata", "CreatedAt", "UpdatedAt")
            VALUES (v_order_id, c.user_id, c.amount, 'XAF', 'completed', 'notchpay',
                    c.ref, 'mtn', 'Recharge du portefeuille WinPlus',
                    v_now, v_now, v_now, 0,
                    'Crédit de test 17.13 / 17.15 (script manuel credit-test-wallets.sql)', v_now, v_now);
        END IF;

        -- 4. Écriture du registre (WalletService.SyncWalletTopUpAsync).
        v_key := 'recharge:topup:' || v_topup_id;
        IF NOT EXISTS (SELECT 1 FROM "WalletTransactions" t WHERE t."IdempotencyKey" = v_key) THEN
            INSERT INTO "WalletTransactions" ("OwnerType", "OwnerId", "EntryType", "Amount", "Status",
                                              "SourceType", "SourceId", "IdempotencyKey", "ReversesEntryId",
                                              "Description", "CreatedByUserId", "OccurredAt", "CreatedAt",
                                              "SettledAt", "ExpiresAt")
            VALUES (v_owner_type, c.user_id, 'Recharge', c.amount, 'confirmed',
                    'WalletTopUp', v_topup_id, v_key, NULL,
                    'Recharge Mobile Money', NULL, v_now, v_now,
                    v_now, NULL);
            RAISE NOTICE 'Crédit % : % XAF crédités au compte % (portefeuille %, recharge %)', c.ref, c.amount, c.user_id, v_owner_type, v_topup_id;
        ELSE
            RAISE NOTICE 'Crédit % : déjà appliqué (recharge %), rien à faire', c.ref, v_topup_id;
        END IF;
    END LOOP;
END $$;

COMMIT;

-- Contrôle : solde disponible (écritures confirmées hors trésorerie plateforme).
SELECT u."Id" AS user_id, u."Role" AS role,
       COALESCE(SUM(t."Amount") FILTER (WHERE t."Status" = 'confirmed'), 0) AS available_xaf,
       COUNT(t."Id") FILTER (WHERE t."IdempotencyKey" IN (
           SELECT 'recharge:topup:' || w."Id" FROM "WalletTopUps" w
           WHERE w."ClientRequestId" LIKE 'TEST-CREDIT-2026-10-12-%')) AS test_credit_entries
  FROM "Users" u
  LEFT JOIN "WalletTransactions" t ON t."OwnerId" = u."Id" AND t."OwnerType" <> 'platform'
 WHERE u."Id" IN (30, 26, 50)
 GROUP BY u."Id", u."Role"
 ORDER BY u."Id";
