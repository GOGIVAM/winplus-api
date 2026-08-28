-- ============================================================================
--  WinPlus  Migration sprints S1 → S7
--  À exécuter une seule fois sur la base PostgreSQL.
--  Idempotente : chaque bloc vérifie l'existence avant de créer.
-- ============================================================================

BEGIN;

-- ── 1. Colonnes ajoutées sur des tables existantes ─────────────────────────

ALTER TABLE "Users"
    ADD COLUMN IF NOT EXISTS "InstitutionId" integer NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_Users_Institutions_InstitutionId'
    ) THEN
        ALTER TABLE "Users"
            ADD CONSTRAINT "FK_Users_Institutions_InstitutionId"
            FOREIGN KEY ("InstitutionId") REFERENCES "Institutions"("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_Users_InstitutionId" ON "Users" ("InstitutionId");

ALTER TABLE "CourseContents"
    ADD COLUMN IF NOT EXISTS "CreatedByUserId" integer NULL,
    ADD COLUMN IF NOT EXISTS "Status" character varying(20) NOT NULL DEFAULT 'published';

CREATE INDEX IF NOT EXISTS "IX_CourseContents_CreatedByUserId" ON "CourseContents" ("CreatedByUserId");
CREATE INDEX IF NOT EXISTS "IX_CourseContents_Status"          ON "CourseContents" ("Status");

ALTER TABLE "PricingPlans"
    ADD COLUMN IF NOT EXISTS "MonthlyCredits"      integer NULL,
    ADD COLUMN IF NOT EXISTS "TeacherRevenueShare" numeric(4,3) NULL,
    ADD COLUMN IF NOT EXISTS "MaxChildren"         integer NULL;

-- ── 2. Groupes d'étude (S7-2) ──────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS "StudyGroups" (
    "Id"             serial PRIMARY KEY,
    "OwnerId"        integer NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Name"           character varying(80)  NOT NULL,
    "Subject"        character varying(100) NULL,
    "Description"    character varying(500) NULL,
    "JoinCode"       character varying(10)  NOT NULL,
    "IsActive"       boolean NOT NULL DEFAULT true,
    "CreatedAt"      timestamp with time zone NOT NULL DEFAULT now(),
    "LastActivityAt" timestamp with time zone NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_StudyGroups_JoinCode" ON "StudyGroups" ("JoinCode");
CREATE INDEX IF NOT EXISTS "IX_StudyGroups_OwnerId"        ON "StudyGroups" ("OwnerId");

CREATE TABLE IF NOT EXISTS "StudyGroupMembers" (
    "Id"           serial PRIMARY KEY,
    "StudyGroupId" integer NOT NULL REFERENCES "StudyGroups"("Id") ON DELETE CASCADE,
    "UserId"       integer NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "Role"         character varying(20) NOT NULL DEFAULT 'member',
    "JoinedAt"     timestamp with time zone NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_StudyGroupMembers_Group_User"
    ON "StudyGroupMembers" ("StudyGroupId", "UserId");

-- ── 3. Notes et tags de révision (S7-1) ────────────────────────────────────

CREATE TABLE IF NOT EXISTS "RevisionNotes" (
    "Id"        serial PRIMARY KEY,
    "UserId"    integer NOT NULL REFERENCES "Users"("Id")    ON DELETE CASCADE,
    "SubjectId" integer NOT NULL REFERENCES "Subjects"("Id") ON DELETE CASCADE,
    "Content"   character varying(300) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NULL
);

CREATE INDEX IF NOT EXISTS "IX_RevisionNotes_User_Subject" ON "RevisionNotes" ("UserId", "SubjectId");

CREATE TABLE IF NOT EXISTS "RevisionTags" (
    "Id"        serial PRIMARY KEY,
    "UserId"    integer NOT NULL REFERENCES "Users"("Id")    ON DELETE CASCADE,
    "SubjectId" integer NOT NULL REFERENCES "Subjects"("Id") ON DELETE CASCADE,
    "Label"     character varying(40) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_RevisionTags_User_Subject_Label"
    ON "RevisionTags" ("UserId", "SubjectId", "Label");

-- ── 4. Questions ratées / quiz de révision (S7-4) ──────────────────────────

CREATE TABLE IF NOT EXISTS "QuizMistakes" (
    "Id"            serial PRIMARY KEY,
    "UserId"        integer NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "QuizId"        integer NULL REFERENCES "Quizzes"("Id") ON DELETE SET NULL,
    "QuizAttemptId" integer NULL,
    "Subject"       character varying(100)  NULL,
    "Question"      character varying(1000) NOT NULL,
    "GivenAnswer"   character varying(500)  NULL,
    "CorrectAnswer" character varying(500)  NULL,
    "IsResolved"    boolean NOT NULL DEFAULT false,
    "ResolvedAt"    timestamp with time zone NULL,
    "CreatedAt"     timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_QuizMistakes_User_Resolved" ON "QuizMistakes" ("UserId", "IsResolved");
CREATE INDEX IF NOT EXISTS "IX_QuizMistakes_CreatedAt"     ON "QuizMistakes" ("CreatedAt");

-- ── 5. Élèves d'une classe enseignant (S4-4) ───────────────────────────────

CREATE TABLE IF NOT EXISTS "TeacherClassStudents" (
    "Id"             serial PRIMARY KEY,
    "TeacherClassId" integer NOT NULL REFERENCES "TeacherClasses"("Id") ON DELETE CASCADE,
    "StudentId"      integer NOT NULL REFERENCES "Users"("Id")          ON DELETE CASCADE,
    "AddedAt"        timestamp with time zone NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TeacherClassStudents_Class_Student"
    ON "TeacherClassStudents" ("TeacherClassId", "StudentId");

-- ── 6. Crédits mensuels parent (S3-1 / S3-5) ───────────────────────────────

CREATE TABLE IF NOT EXISTS "ParentCreditLedgers" (
    "Id"          serial PRIMARY KEY,
    "ParentId"    integer NOT NULL REFERENCES "Users"("Id") ON DELETE CASCADE,
    "EntryType"   character varying(20) NOT NULL,
    "Amount"      numeric(12,2) NOT NULL,
    "ChildId"     integer NULL REFERENCES "Users"("Id") ON DELETE SET NULL,
    "OrderId"     integer NULL,
    "Label"       character varying(300) NULL,
    "PeriodStart" timestamp with time zone NOT NULL,
    "CreatedAt"   timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_ParentCreditLedgers_Parent_Period"
    ON "ParentCreditLedgers" ("ParentId", "PeriodStart");

-- ── 7. Annuaire institution (S5-1 / S5-2) ──────────────────────────────────

CREATE TABLE IF NOT EXISTS "InstitutionStudents" (
    "Id"              serial PRIMARY KEY,
    "InstitutionId"   integer NOT NULL REFERENCES "Institutions"("Id") ON DELETE CASCADE,
    "StudentId"       integer NOT NULL REFERENCES "Users"("Id")        ON DELETE CASCADE,
    "GroupName"       character varying(100) NULL,
    "Level"           character varying(100) NULL,
    "MatriculeNumber" character varying(50)  NULL,
    "IsActive"        boolean NOT NULL DEFAULT true,
    "CreatedAt"       timestamp with time zone NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_InstitutionStudents_Institution_Student"
    ON "InstitutionStudents" ("InstitutionId", "StudentId");
CREATE INDEX IF NOT EXISTS "IX_InstitutionStudents_InstitutionId"
    ON "InstitutionStudents" ("InstitutionId");

COMMIT;

-- ============================================================================
--  Paramétrage métier  À FAIRE PAR L'ADMINISTRATEUR
--  Les crédits mensuels, la part enseignant et le nombre d'enfants ne sont
--  pas codés dans l'API : ils se règlent ici, plan par plan. Adaptez les
--  libellés à ceux réellement présents dans votre table PricingPlans.
-- ============================================================================

-- UPDATE "PricingPlans" SET "MonthlyCredits" = 10000, "MaxChildren" = 1 WHERE "Category" = 'parents' AND "Name" ILIKE '%basique%';
-- UPDATE "PricingPlans" SET "MonthlyCredits" = 20000, "MaxChildren" = 3 WHERE "Category" = 'parents' AND "Name" ILIKE '%complet%';
-- UPDATE "PricingPlans" SET "MonthlyCredits" = 40000, "MaxChildren" = 5 WHERE "Category" = 'parents' AND "Name" ILIKE '%famille%';

-- UPDATE "PricingPlans" SET "TeacherRevenueShare" = 0.700 WHERE "Category" = 'teachers' AND "Name" ILIKE '%fondateur%';
-- UPDATE "PricingPlans" SET "TeacherRevenueShare" = 0.750 WHERE "Category" = 'teachers' AND "Name" ILIKE '%pro%';
-- UPDATE "PricingPlans" SET "TeacherRevenueShare" = 0.800 WHERE "Category" = 'teachers' AND "Name" ILIKE '%expert%';

-- Reprise de l'historique : attribuer les publications existantes à leur auteur
-- si vous disposez d'une trace (sinon laisser NULL, elles n'apparaîtront chez
-- personne plutôt que chez tout le monde).
