-- ════════════════════════════════════════════════════════════════════════════
-- SQL_FixChatbotColumns.sql
--
-- Cause confirmée du 500 sur GET /api/admin/winai/stats :
--
--   Npgsql.PostgresException 42703: column m.GenerationTimeMs does not exist
--   at Backend.Controllers.AdminController.GetWinAIStats() … line 811
--
-- Les tables Conversations / Messages / ChatbotContexts existent, mais elles
-- ont été créées sans le schéma complet des entités : plusieurs colonnes
-- déclarées dans Models/Entities/{Message,Conversation,ChatbotContext}.cs sont
-- absentes en base. EF Core génère du SQL qui les référence → 42703.
--
-- Ce script ajoute toutes les colonnes manquantes. Idempotent
-- (ADD COLUMN IF NOT EXISTS) : rejouable sans risque, ne touche pas aux
-- colonnes déjà présentes ni aux données.
--
-- Exécution :
--   psql -h localhost -U cademi -d winplus_db -f SQL_FixChatbotColumns.sql
--
-- Puis redémarrer l'API :
--   sudo systemctl restart winplus-backend
-- ════════════════════════════════════════════════════════════════════════════

BEGIN;

-- ── Messages ──────────────────────────────────────────────────────────────
-- C'est ici que se trouve la colonne qui provoque le 500.
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "Role"             character varying(20)   NOT NULL DEFAULT 'user';
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "Content"          text                    NOT NULL DEFAULT '';
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "Attachments"      jsonb;
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "TokensUsed"       integer;
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "FeedbackRating"   integer;
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "FeedbackComment"  character varying(1000);
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "GenerationTimeMs" integer;
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "CreatedAt"        timestamp with time zone NOT NULL DEFAULT NOW();
ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "IsDeleted"        boolean                 NOT NULL DEFAULT FALSE;

-- ── Conversations ─────────────────────────────────────────────────────────
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "Title"         character varying(255)   NOT NULL DEFAULT 'Nouvelle conversation';
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "Tags"          jsonb;
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "Metadata"      jsonb;
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "IsActive"      boolean                  NOT NULL DEFAULT TRUE;
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "LastMessageAt" timestamp with time zone;
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "MessageCount"  integer                  NOT NULL DEFAULT 0;
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "CreatedAt"     timestamp with time zone NOT NULL DEFAULT NOW();
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "UpdatedAt"     timestamp with time zone NOT NULL DEFAULT NOW();
ALTER TABLE "Conversations" ADD COLUMN IF NOT EXISTS "IsDeleted"     boolean                  NOT NULL DEFAULT FALSE;

-- ── ChatbotContexts ───────────────────────────────────────────────────────
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "EducationLevel"    character varying(50);
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "Grade"             character varying(50);
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "UserObjectives"    jsonb;
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "EnrolledSubjects"  jsonb;
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "RecentActivity"    jsonb;
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "NavigationHistory" jsonb;
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "Preferences"       jsonb;
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "Strengths"         jsonb;
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "Weaknesses"        jsonb;
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "LearningStyle"     character varying(50);
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "CreatedAt"         timestamp with time zone NOT NULL DEFAULT NOW();
ALTER TABLE "ChatbotContexts" ADD COLUMN IF NOT EXISTS "UpdatedAt"         timestamp with time zone NOT NULL DEFAULT NOW();

-- Index utilisés par les requêtes admin
CREATE INDEX IF NOT EXISTS "IX_Conversations_IsActive"      ON "Conversations" ("IsActive");
CREATE INDEX IF NOT EXISTS "IX_Conversations_IsDeleted"     ON "Conversations" ("IsDeleted");
CREATE INDEX IF NOT EXISTS "IX_Conversations_LastMessageAt" ON "Conversations" ("LastMessageAt");
CREATE INDEX IF NOT EXISTS "IX_Messages_IsDeleted"          ON "Messages" ("IsDeleted");
CREATE INDEX IF NOT EXISTS "IX_Messages_CreatedAt"          ON "Messages" ("CreatedAt");

COMMIT;

-- ── Vérification : doit renvoyer les 11 colonnes de Messages ──────────────
SELECT table_name, column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name IN ('Conversations', 'Messages', 'ChatbotContexts')
ORDER BY table_name, ordinal_position;

-- ── Contrôle ciblé : plus aucune ligne = plus aucune colonne manquante ────
WITH attendu(t, c) AS (VALUES
    ('Messages','Role'), ('Messages','Content'), ('Messages','Attachments'),
    ('Messages','TokensUsed'), ('Messages','FeedbackRating'),
    ('Messages','FeedbackComment'), ('Messages','GenerationTimeMs'),
    ('Messages','CreatedAt'), ('Messages','IsDeleted'),
    ('Conversations','Title'), ('Conversations','Tags'), ('Conversations','Metadata'),
    ('Conversations','IsActive'), ('Conversations','LastMessageAt'),
    ('Conversations','MessageCount'), ('Conversations','CreatedAt'),
    ('Conversations','UpdatedAt'), ('Conversations','IsDeleted')
)
SELECT a.t AS table_manquante, a.c AS colonne_manquante
FROM attendu a
LEFT JOIN information_schema.columns ic
       ON ic.table_name = a.t AND ic.column_name = a.c
WHERE ic.column_name IS NULL;
