-- ============================================================================
--  WinPlus  Module Formations
--  Migration idempotente (IF NOT EXISTS partout).
--  À exécuter UNE SEULE FOIS sur winplus_db.
--
--  IMPORTANT : Les tables existantes utilisent le PascalCase avec guillemets
--  PostgreSQL ("Users", "OrderItems", etc.). Toutes les nouvelles tables
--  suivent la même convention.
-- ============================================================================

BEGIN;

-- ── 1. Table Courses ─────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS "Courses" (
    "Id"               SERIAL PRIMARY KEY,
    "InstructorId"     INTEGER      NOT NULL,
    "Title"            TEXT         NOT NULL,
    "Slug"             TEXT         NOT NULL,
    "Description"      TEXT,
    "ShortDescription" TEXT,
    "ThumbnailUrl"     TEXT,
    "PreviewVideoUrl"  TEXT,
    "Language"         TEXT         NOT NULL DEFAULT 'fr',
    "Level"            TEXT,
    "Category"         TEXT,
    "Tags"             TEXT[]       NOT NULL DEFAULT '{}',
    "Price"            NUMERIC(12,2) NOT NULL DEFAULT 0,
    "IsFree"           BOOLEAN      NOT NULL DEFAULT FALSE,
    "IsIncludedInSub"  BOOLEAN      NOT NULL DEFAULT FALSE,
    "Status"           TEXT         NOT NULL DEFAULT 'draft',
    "Requirements"     TEXT[]       NOT NULL DEFAULT '{}',
    "Objectives"       TEXT[]       NOT NULL DEFAULT '{}',
    "TotalDurationMin" INTEGER      NOT NULL DEFAULT 0,
    "LessonsCount"     INTEGER      NOT NULL DEFAULT 0,
    "EnrolledCount"    INTEGER      NOT NULL DEFAULT 0,
    "AvgRating"        NUMERIC(3,2) NOT NULL DEFAULT 0,
    "ReviewsCount"     INTEGER      NOT NULL DEFAULT 0,
    "CertificateEnabled" BOOLEAN    NOT NULL DEFAULT TRUE,
    "PublishedAt"      TIMESTAMPTZ,
    "CreatedAt"        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt"        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT "UQ_Courses_Slug" UNIQUE ("Slug")
);

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_Courses_Users_InstructorId'
    ) THEN
        ALTER TABLE "Courses"
            ADD CONSTRAINT "FK_Courses_Users_InstructorId"
            FOREIGN KEY ("InstructorId") REFERENCES "Users"("Id") ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_Courses_InstructorId" ON "Courses" ("InstructorId");
CREATE INDEX IF NOT EXISTS "IX_Courses_Status"       ON "Courses" ("Status");
CREATE INDEX IF NOT EXISTS "IX_Courses_Category"     ON "Courses" ("Category");

-- ── 2. Table CourseSections ──────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS "CourseSections" (
    "Id"          SERIAL PRIMARY KEY,
    "CourseId"    INTEGER      NOT NULL,
    "Title"       TEXT         NOT NULL,
    "Description" TEXT,
    "Position"    INTEGER      NOT NULL DEFAULT 0,
    "CreatedAt"   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt"   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_CourseSections_Courses_CourseId'
    ) THEN
        ALTER TABLE "CourseSections"
            ADD CONSTRAINT "FK_CourseSections_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CourseSections_CourseId" ON "CourseSections" ("CourseId");

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes WHERE indexname = 'UQ_CourseSections_CourseId_Position'
    ) THEN
        CREATE UNIQUE INDEX "UQ_CourseSections_CourseId_Position"
            ON "CourseSections" ("CourseId", "Position");
    END IF;
END $$;

-- ── 3. Table CourseLessons ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS "CourseLessons" (
    "Id"               SERIAL PRIMARY KEY,
    "SectionId"        INTEGER      NOT NULL,
    "CourseId"         INTEGER      NOT NULL,
    "Title"            TEXT         NOT NULL,
    "LessonType"       TEXT         NOT NULL DEFAULT 'video',
    "Description"      TEXT,
    "VideoUrl"         TEXT,
    "VideoDurationSec" INTEGER      NOT NULL DEFAULT 0,
    "ArticleContent"   TEXT,
    "FileUrl"          TEXT,
    "FileName"         TEXT,
    "Position"         INTEGER      NOT NULL DEFAULT 0,
    "IsPreview"        BOOLEAN      NOT NULL DEFAULT FALSE,
    "IsPublished"      BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt"        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_CourseLessons_CourseSections_SectionId'
    ) THEN
        ALTER TABLE "CourseLessons"
            ADD CONSTRAINT "FK_CourseLessons_CourseSections_SectionId"
            FOREIGN KEY ("SectionId") REFERENCES "CourseSections"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_CourseLessons_Courses_CourseId'
    ) THEN
        ALTER TABLE "CourseLessons"
            ADD CONSTRAINT "FK_CourseLessons_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CourseLessons_SectionId" ON "CourseLessons" ("SectionId");
CREATE INDEX IF NOT EXISTS "IX_CourseLessons_CourseId"  ON "CourseLessons" ("CourseId");

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes WHERE indexname = 'UQ_CourseLessons_SectionId_Position'
    ) THEN
        CREATE UNIQUE INDEX "UQ_CourseLessons_SectionId_Position"
            ON "CourseLessons" ("SectionId", "Position");
    END IF;
END $$;

-- ── 4. Table CourseEnrollments ───────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS "CourseEnrollments" (
    "Id"              SERIAL PRIMARY KEY,
    "UserId"          INTEGER      NOT NULL,
    "CourseId"        INTEGER      NOT NULL,
    "AccessType"      TEXT         NOT NULL DEFAULT 'free',
    "IsActive"        BOOLEAN      NOT NULL DEFAULT TRUE,
    "ProgressPercent" NUMERIC(5,2) NOT NULL DEFAULT 0,
    "EnrolledAt"      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "LastAccessedAt"  TIMESTAMPTZ,
    "CompletedAt"     TIMESTAMPTZ,
    "CertificateUrl"  TEXT,
    "UpdatedAt"       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_CourseEnrollments_Users_UserId'
    ) THEN
        ALTER TABLE "CourseEnrollments"
            ADD CONSTRAINT "FK_CourseEnrollments_Users_UserId"
            FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_CourseEnrollments_Courses_CourseId'
    ) THEN
        ALTER TABLE "CourseEnrollments"
            ADD CONSTRAINT "FK_CourseEnrollments_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes WHERE indexname = 'UQ_CourseEnrollments_UserId_CourseId'
    ) THEN
        CREATE UNIQUE INDEX "UQ_CourseEnrollments_UserId_CourseId"
            ON "CourseEnrollments" ("UserId", "CourseId");
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CourseEnrollments_UserId"   ON "CourseEnrollments" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_CourseEnrollments_CourseId" ON "CourseEnrollments" ("CourseId");

-- ── 5. Table LessonProgress ──────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS "LessonProgress" (
    "Id"              SERIAL PRIMARY KEY,
    "UserId"          INTEGER      NOT NULL,
    "LessonId"        INTEGER      NOT NULL,
    "CourseId"        INTEGER      NOT NULL,
    "IsCompleted"     BOOLEAN      NOT NULL DEFAULT FALSE,
    "WatchTimeSec"    INTEGER      NOT NULL DEFAULT 0,
    "LastPositionSec" INTEGER      NOT NULL DEFAULT 0,
    "CompletedAt"     TIMESTAMPTZ,
    "UpdatedAt"       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_LessonProgress_Users_UserId'
    ) THEN
        ALTER TABLE "LessonProgress"
            ADD CONSTRAINT "FK_LessonProgress_Users_UserId"
            FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_LessonProgress_CourseLessons_LessonId'
    ) THEN
        ALTER TABLE "LessonProgress"
            ADD CONSTRAINT "FK_LessonProgress_CourseLessons_LessonId"
            FOREIGN KEY ("LessonId") REFERENCES "CourseLessons"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_LessonProgress_Courses_CourseId'
    ) THEN
        ALTER TABLE "LessonProgress"
            ADD CONSTRAINT "FK_LessonProgress_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes WHERE indexname = 'UQ_LessonProgress_UserId_LessonId'
    ) THEN
        CREATE UNIQUE INDEX "UQ_LessonProgress_UserId_LessonId"
            ON "LessonProgress" ("UserId", "LessonId");
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_LessonProgress_UserId"   ON "LessonProgress" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_LessonProgress_CourseId" ON "LessonProgress" ("CourseId");

-- ── 6. Table CourseReviews ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS "CourseReviews" (
    "Id"         SERIAL PRIMARY KEY,
    "CourseId"   INTEGER      NOT NULL,
    "UserId"     INTEGER      NOT NULL,
    "Rating"     SMALLINT     NOT NULL CHECK ("Rating" BETWEEN 1 AND 5),
    "Comment"    TEXT,
    "IsVerified" BOOLEAN      NOT NULL DEFAULT FALSE,
    "CreatedAt"  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt"  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_CourseReviews_Courses_CourseId'
    ) THEN
        ALTER TABLE "CourseReviews"
            ADD CONSTRAINT "FK_CourseReviews_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_CourseReviews_Users_UserId'
    ) THEN
        ALTER TABLE "CourseReviews"
            ADD CONSTRAINT "FK_CourseReviews_Users_UserId"
            FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE;
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes WHERE indexname = 'UQ_CourseReviews_UserId_CourseId'
    ) THEN
        CREATE UNIQUE INDEX "UQ_CourseReviews_UserId_CourseId"
            ON "CourseReviews" ("UserId", "CourseId");
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CourseReviews_CourseId" ON "CourseReviews" ("CourseId");

-- ── 7. Colonne CourseId dans OrderItems ──────────────────────────────────────

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "CourseId" INTEGER NULL;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_OrderItems_Courses_CourseId'
    ) THEN
        ALTER TABLE "OrderItems"
            ADD CONSTRAINT "FK_OrderItems_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE SET NULL;
    END IF;
END $$;

-- ── 8. Triggers updated_at ───────────────────────────────────────────────────
-- La fonction update_updated_at_column() existe déjà (créée dans les sprints S1-S7).

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_courses_updated_at') THEN
        CREATE TRIGGER update_courses_updated_at
            BEFORE UPDATE ON "Courses"
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_sections_updated_at') THEN
        CREATE TRIGGER update_sections_updated_at
            BEFORE UPDATE ON "CourseSections"
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_lessons_updated_at') THEN
        CREATE TRIGGER update_lessons_updated_at
            BEFORE UPDATE ON "CourseLessons"
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_enrollments_updated_at') THEN
        CREATE TRIGGER update_enrollments_updated_at
            BEFORE UPDATE ON "CourseEnrollments"
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_lesson_progress_updated_at') THEN
        CREATE TRIGGER update_lesson_progress_updated_at
            BEFORE UPDATE ON "LessonProgress"
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_reviews_updated_at') THEN
        CREATE TRIGGER update_reviews_updated_at
            BEFORE UPDATE ON "CourseReviews"
            FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
    END IF;
END $$;

-- ── 9. Triggers compteurs automatiques ──────────────────────────────────────

-- LessonsCount
CREATE OR REPLACE FUNCTION sync_course_lessons_count()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    UPDATE "Courses" SET "LessonsCount" = (
        SELECT COUNT(*) FROM "CourseLessons"
        WHERE "CourseId" = COALESCE(NEW."CourseId", OLD."CourseId")
          AND "IsPublished" = TRUE
    ) WHERE "Id" = COALESCE(NEW."CourseId", OLD."CourseId");
    RETURN NULL;
END;
$$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_lessons_count') THEN
        CREATE TRIGGER trg_lessons_count
            AFTER INSERT OR UPDATE OR DELETE ON "CourseLessons"
            FOR EACH ROW EXECUTE FUNCTION sync_course_lessons_count();
    END IF;
END $$;

-- EnrolledCount
CREATE OR REPLACE FUNCTION sync_enrolled_count()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    UPDATE "Courses" SET "EnrolledCount" = (
        SELECT COUNT(*) FROM "CourseEnrollments"
        WHERE "CourseId" = COALESCE(NEW."CourseId", OLD."CourseId")
          AND "IsActive" = TRUE
    ) WHERE "Id" = COALESCE(NEW."CourseId", OLD."CourseId");
    RETURN NULL;
END;
$$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_enrolled_count') THEN
        CREATE TRIGGER trg_enrolled_count
            AFTER INSERT OR UPDATE OR DELETE ON "CourseEnrollments"
            FOR EACH ROW EXECUTE FUNCTION sync_enrolled_count();
    END IF;
END $$;

-- AvgRating + ReviewsCount
CREATE OR REPLACE FUNCTION sync_course_rating()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE v_cid INTEGER;
BEGIN
    v_cid := COALESCE(NEW."CourseId", OLD."CourseId");
    UPDATE "Courses" SET
        "AvgRating"    = COALESCE((SELECT AVG("Rating") FROM "CourseReviews" WHERE "CourseId" = v_cid), 0),
        "ReviewsCount" = (SELECT COUNT(*) FROM "CourseReviews" WHERE "CourseId" = v_cid)
    WHERE "Id" = v_cid;
    RETURN NULL;
END;
$$;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trg_course_rating') THEN
        CREATE TRIGGER trg_course_rating
            AFTER INSERT OR UPDATE OR DELETE ON "CourseReviews"
            FOR EACH ROW EXECUTE FUNCTION sync_course_rating();
    END IF;
END $$;

-- ── 10. Fonction recalculate_enrollment_progress ─────────────────────────────

CREATE OR REPLACE FUNCTION recalculate_enrollment_progress(p_user_id INTEGER, p_course_id INTEGER)
RETURNS VOID LANGUAGE plpgsql AS $$
DECLARE
    v_total     INTEGER;
    v_completed INTEGER;
    v_percent   NUMERIC(5,2);
BEGIN
    SELECT COUNT(*) INTO v_total
    FROM "CourseLessons"
    WHERE "CourseId" = p_course_id AND "IsPublished" = TRUE;

    IF v_total = 0 THEN RETURN; END IF;

    SELECT COUNT(*) INTO v_completed
    FROM "LessonProgress"
    WHERE "UserId" = p_user_id
      AND "CourseId" = p_course_id
      AND "IsCompleted" = TRUE;

    v_percent := ROUND((v_completed::NUMERIC / v_total) * 100, 2);

    UPDATE "CourseEnrollments" SET
        "ProgressPercent" = v_percent,
        "CompletedAt"     = CASE
            WHEN v_percent >= 100 AND "CompletedAt" IS NULL THEN NOW()
            ELSE "CompletedAt"
        END,
        "UpdatedAt" = NOW()
    WHERE "UserId" = p_user_id AND "CourseId" = p_course_id;
END;
$$;

-- ── Vérification finale ───────────────────────────────────────────────────────

DO $$
DECLARE
    t TEXT;
    tables TEXT[] := ARRAY[
        'Courses', 'CourseSections', 'CourseLessons',
        'CourseEnrollments', 'LessonProgress', 'CourseReviews'
    ];
BEGIN
    FOREACH t IN ARRAY tables LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = t) THEN
            RAISE EXCEPTION 'Table "%" manquante  migration échouée.', t;
        END IF;
    END LOOP;
    RAISE NOTICE 'Migration courses module : OK  6 tables créées.';
END $$;

COMMIT;
