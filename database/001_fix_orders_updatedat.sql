-- ============================================================================
--  001  Orders."UpdatedAt" manquante
-- ============================================================================
--  Erreur observée dans les logs backend :
--
--    Npgsql.PostgresException: 42703: column o.UpdatedAt does not exist
--    at Backend.Repositories.UserRepository.GetByIdAsync(Int32 id)
--
--  Conséquence en cascade : GET /api/users/me renvoie 404, car le repository
--  lève avant de retourner l'utilisateur. Le modèle EF `Order` déclare
--  UpdatedAt, la table PostgreSQL ne l'a pas  une migration n'a pas été
--  appliquée en production.
--
--  À exécuter sur la base de production, dans une transaction.
-- ============================================================================

BEGIN;

ALTER TABLE "Orders"
  ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone;

-- Valeur de départ cohérente pour les commandes existantes.
UPDATE "Orders"
   SET "UpdatedAt" = COALESCE("CompletedDate", "OrderDate", "CreatedAt")
 WHERE "UpdatedAt" IS NULL;

COMMIT;

-- ── Vérification ────────────────────────────────────────────────────────────
-- SELECT column_name, data_type, is_nullable
--   FROM information_schema.columns
--  WHERE table_name = 'Orders'
--  ORDER BY ordinal_position;

-- ── Si d'autres colonnes manquent ───────────────────────────────────────────
-- Compare le modèle EF et la table, puis ajoute-les de la même façon. La
-- requête ci-dessous liste les colonnes réellement présentes :
--
--   SELECT table_name, column_name
--     FROM information_schema.columns
--    WHERE table_schema = 'public'
--      AND table_name IN ('Orders', 'Users', 'Enrollments')
--    ORDER BY table_name, ordinal_position;
--
-- Le bon réflexe ensuite : régénérer et appliquer les migrations EF
-- (`dotnet ef migrations add SyncOrders` puis `dotnet ef database update`)
-- pour que le schéma et le modèle ne divergent plus.
