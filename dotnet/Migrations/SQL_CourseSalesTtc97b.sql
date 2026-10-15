-- Migration: CourseSalesTtc97b
-- Lot 9.7b, chantier B (suivi 18.16 et 18.21, étude ETUDE_9_7B_ABONNEMENTS.md
-- section 4) : formations vendues seules, prix TTC saisis.
--
--   1. Prix TTC (18.21, F11). Le prix saisi devient le prix payé. Règle : le
--      nouveau prix TTC est le prix réellement payé avant le 9.7b, soit
--      HT + arrondi(HT × 19,25 %) à l'XAF (arrondi au plus proche, moitié vers
--      le haut, comme VatPolicy). Exemple : 5 000 HT → 5 963 TTC ; le serveur
--      en déduit HT = arrondi(5 963 / 1,1925) = 5 000 et TVA = 963 : le revenu
--      du vendeur et la commission restent exactement ceux d'avant. Concerne
--      Subjects (épreuves, livres), Courses et les lignes de panier en cours
--      (CartItems). Lignes de commande (OrderItems.PriceAtPurchase) inchangées :
--      elles restent le HT de la vente. APPLIQUÉ UNE SEULE FOIS : garde sur
--      __EFMigrationsHistory (un second passage majorerait encore les prix).
--   2. Formations jamais incluses dans un abonnement (18.16, F6) :
--      IsIncludedInSub = false partout.
--   3. Inscriptions « subscription » (F7) : converties en « purchase » si une
--      commande payée couvre la formation, en « free » si la formation est
--      gratuite, désactivées sinon (progression conservée).
--   4. Gratuit et prix réconciliés (F10) : prix nul ⇒ gratuite, gratuite ⇒ prix 0.
--   5. Achats payés sans inscription (F1, F8) : inscription « purchase » créée
--      pour le bénéficiaire (l'enfant pour un achat parent, « purchase-for-child:<id> »
--      dans Orders.Notes), réactivée si elle était inactive, OrderId renseigné.
--
-- Étapes 2 à 5 naturellement idempotentes (rejouables sans effet).
-- Aucun prérequis d'ordre : ne dépend que de tables historiques (Subjects,
-- Courses, CartItems, Orders, OrderItems, CourseEnrollments, colonne OrderId
-- ajoutée par SQL_AddCourseEnrollmentOrderIdLessonProgressCreatedAt.sql, dont
-- l'ajout est rejoué ici par sécurité).
-- Équivalent C# : Services/VatPolicy.cs, Services/CourseSaleService.cs.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);

ALTER TABLE "CourseEnrollments" ADD COLUMN IF NOT EXISTS "OrderId" INTEGER NULL;

-- ── 1. Prix TTC (une seule fois) ─────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015120000_CourseSalesTtc97b') THEN
        UPDATE "Subjects"
           SET "Price" = ROUND("Price") + ROUND(ROUND("Price") * 0.1925)
         WHERE "Price" > 0;

        UPDATE "Courses"
           SET "Price" = ROUND("Price") + ROUND(ROUND("Price") * 0.1925)
         WHERE "Price" > 0;

        UPDATE "CartItems"
           SET "Price" = ROUND("Price") + ROUND(ROUND("Price") * 0.1925)
         WHERE "Price" > 0;

        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ('20261015120000_CourseSalesTtc97b', '8.0.0')
        ON CONFLICT DO NOTHING;
    END IF;
END $$;

-- ── 2. Plus d'inclusion dans l'abonnement ────────────────────────────────
UPDATE "Courses" SET "IsIncludedInSub" = FALSE WHERE "IsIncludedInSub";

-- ── 4. Gratuit et prix réconciliés (avant l'étape 3, qui lit IsFree) ────
UPDATE "Courses" SET "IsFree" = TRUE WHERE "Price" <= 0 AND NOT "IsFree";
UPDATE "Courses" SET "Price" = 0 WHERE "IsFree" AND "Price" <> 0;

-- Bénéficiaire et commandes payées d'une formation (vue de travail).
DROP TABLE IF EXISTS tmp_paid_course_orders;
CREATE TEMP TABLE tmp_paid_course_orders AS
SELECT oi."CourseId" AS course_id,
       CASE WHEN o."Notes" LIKE 'purchase-for-child:%'
                 AND SUBSTRING(o."Notes" FROM 20) ~ '^[0-9]+$'
            THEN SUBSTRING(o."Notes" FROM 20)::INTEGER
            ELSE o."UserId" END AS beneficiary_id,
       MAX(o."Id") AS order_id
  FROM "OrderItems" oi
  JOIN "Orders" o ON o."Id" = oi."OrderId"
 WHERE oi."CourseId" IS NOT NULL
   AND o."UserId" IS NOT NULL
   AND LOWER(o."Status") IN ('completed', 'paid', 'refund_requested')
 GROUP BY 1, 2;

-- ── 3. Inscriptions « subscription » ─────────────────────────────────────
UPDATE "CourseEnrollments" e
   SET "AccessType" = 'purchase', "OrderId" = p.order_id
  FROM tmp_paid_course_orders p
 WHERE LOWER(e."AccessType") = 'subscription'
   AND p.course_id = e."CourseId" AND p.beneficiary_id = e."UserId";

UPDATE "CourseEnrollments" e
   SET "AccessType" = 'free', "OrderId" = NULL
  FROM "Courses" c
 WHERE LOWER(e."AccessType") = 'subscription'
   AND c."Id" = e."CourseId" AND c."IsFree";

UPDATE "CourseEnrollments"
   SET "IsActive" = FALSE
 WHERE LOWER("AccessType") = 'subscription' AND "IsActive";

-- ── 5. Achats payés sans inscription active ─────────────────────────────
UPDATE "CourseEnrollments" e
   SET "IsActive" = TRUE, "AccessType" = 'purchase', "OrderId" = p.order_id
  FROM tmp_paid_course_orders p
 WHERE p.course_id = e."CourseId" AND p.beneficiary_id = e."UserId"
   AND (NOT e."IsActive" OR e."OrderId" IS NULL OR LOWER(e."AccessType") <> 'purchase');

INSERT INTO "CourseEnrollments" ("UserId", "CourseId", "EnrolledAt", "AccessType", "OrderId", "ProgressPercent", "IsActive")
SELECT p.beneficiary_id, p.course_id, NOW(), 'purchase', p.order_id, 0, TRUE
  FROM tmp_paid_course_orders p
  JOIN "Users" u ON u."Id" = p.beneficiary_id
 WHERE NOT EXISTS (SELECT 1 FROM "CourseEnrollments" e
                    WHERE e."UserId" = p.beneficiary_id AND e."CourseId" = p.course_id);

DROP TABLE IF EXISTS tmp_paid_course_orders;
