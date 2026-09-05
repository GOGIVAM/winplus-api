-- WinPlus  Seed données de test 2026-09-01
-- Crée des comptes de test avec le même mot de passe que votre compte admin.
-- À appliquer UNE seule fois sur l'environnement de test.
-- psql -U winplus -d winplus -f 2026-09-01_seed_test.sql

BEGIN;

-- ─── Comptes de test ─────────────────────────────────────────────────────────
-- Mot de passe = identique à votre compte admin existant (récupéré par sous-requête)
-- Pour connaître le mot de passe utilisé : c'est celui de l'admin avec l'id le plus bas.

DO $$
DECLARE
  admin_hash TEXT;
  admin_id   INTEGER;
BEGIN
  SELECT "Id", "PasswordHash" INTO admin_id, admin_hash
  FROM public."Users"
  WHERE "Role" = 'admin'
  ORDER BY "Id" ASC
  LIMIT 1;

  IF admin_hash IS NULL THEN
    RAISE EXCEPTION 'Aucun admin trouvé  impossible de récupérer le hash du mot de passe.';
  END IF;

  -- ── Étudiant de test ──
  INSERT INTO public."Users" (
    "FirstName","LastName","Email","PasswordHash","Role",
    "IsEmailVerified","IsActive","CreatedAt","UpdatedAt"
  )
  SELECT
    'Kouam', 'Test', 'etudiant@test.winplus.cm', admin_hash, 'student',
    TRUE, TRUE, NOW(), NOW()
  WHERE NOT EXISTS (
    SELECT 1 FROM public."Users" WHERE "Email" = 'etudiant@test.winplus.cm'
  );

  -- ── Professeur de test ──
  INSERT INTO public."Users" (
    "FirstName","LastName","Email","PasswordHash","Role",
    "IsEmailVerified","IsActive","CreatedAt","UpdatedAt"
  )
  SELECT
    'Njoya', 'Professeur', 'prof@test.winplus.cm', admin_hash, 'teacher',
    TRUE, TRUE, NOW(), NOW()
  WHERE NOT EXISTS (
    SELECT 1 FROM public."Users" WHERE "Email" = 'prof@test.winplus.cm'
  );

  -- ── Parent de test ──
  INSERT INTO public."Users" (
    "FirstName","LastName","Email","PasswordHash","Role",
    "IsEmailVerified","IsActive","CreatedAt","UpdatedAt"
  )
  SELECT
    'Biya', 'Parent', 'parent@test.winplus.cm', admin_hash, 'parent',
    TRUE, TRUE, NOW(), NOW()
  WHERE NOT EXISTS (
    SELECT 1 FROM public."Users" WHERE "Email" = 'parent@test.winplus.cm'
  );

  -- ── Institution de test ──
  INSERT INTO public."Users" (
    "FirstName","LastName","Email","PasswordHash","Role",
    "IsEmailVerified","IsActive","CreatedAt","UpdatedAt"
  )
  SELECT
    'Lycée', 'Bilingue Test', 'institution@test.winplus.cm', admin_hash, 'institution',
    TRUE, TRUE, NOW(), NOW()
  WHERE NOT EXISTS (
    SELECT 1 FROM public."Users" WHERE "Email" = 'institution@test.winplus.cm'
  );

  RAISE NOTICE 'Comptes de test créés avec le hash du compte admin (id=%).',  admin_id;
END$$;

-- ─── Sujet publié de test ─────────────────────────────────────────────────────
INSERT INTO public."Subjects" (
  "Title","Category","Description",
  "Price","IsPublished","IsDeleted","EnrollmentCount","AverageRating","TotalRatings",
  "CreatedAt","UpdatedAt"
)
SELECT
  'Mathématiques  Épreuve BAC C 2025',
  'Mathématiques',
  'Épreuve officielle de mathématiques du BAC C session 2025  Terminale C.',
  500,
  TRUE,
  FALSE,
  0, 0.00, 0,
  NOW(),
  NOW()
WHERE NOT EXISTS (
  SELECT 1 FROM public."Subjects" WHERE "Title" = 'Mathématiques  Épreuve BAC C 2025'
);

-- ─── Promo code de test (si la table existe) ──────────────────────────────────
DO $$
DECLARE
  admin_id INTEGER;
BEGIN
  SELECT "Id" INTO admin_id FROM public."Users" WHERE "Role" = 'admin' ORDER BY "Id" LIMIT 1;

  INSERT INTO public."PromoCodes" (
    "Code","Description","DiscountType","DiscountValue",
    "UsageLimit","ValidFrom","ValidUntil","IsActive","CreatedBy","CreatedAt"
  )
  SELECT
    'TEST2026', 'Code promo de test  20% de réduction', 'Percentage', 20,
    100, NOW(), NOW() + INTERVAL '1 year', TRUE, admin_id, NOW()
  WHERE NOT EXISTS (
    SELECT 1 FROM public."PromoCodes" WHERE "Code" = 'TEST2026'
  );
EXCEPTION WHEN undefined_table THEN
  RAISE NOTICE 'Table PromoCodes non encore créée  relancer après la migration fixes.';
END$$;

COMMIT;

-- ─── Résumé des comptes créés ────────────────────────────────────────────────
SELECT "Id", "Email", "Role", "CreatedAt"
FROM public."Users"
WHERE "Email" LIKE '%@test.winplus.cm'
ORDER BY "Id";
