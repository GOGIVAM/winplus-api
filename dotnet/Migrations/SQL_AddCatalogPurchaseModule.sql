-- Migration: AddCatalogPurchaseModule
-- Module 2 — Catalogue et achat de contenu (professeur_complete.md)
-- Equivalent EF entities: Subject.AuthorUserId, CourseLesson.SourceSubjectId,
-- TeacherClassContent.cs, ConcoursEvent.Tips/FaqJson
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

-- ── Subject.AuthorUserId : attribution d'auteur (US-CAT-01 filtre "Auteur
--    Vérifié", US-CAT-02 compteur d'usage). Null pour le contenu historique
--    sans auteur attribué — corrige aussi GetTeacherRevenuesAsync/GetTeacherStatsAsync
--    qui sommaient les revenus de TOUTE la plateforme faute de ce lien.
ALTER TABLE "Subjects"
    ADD COLUMN IF NOT EXISTS "AuthorUserId" INTEGER;

ALTER TABLE "Subjects"
    DROP CONSTRAINT IF EXISTS "FK_Subjects_Users_AuthorUserId";

ALTER TABLE "Subjects"
    ADD CONSTRAINT "FK_Subjects_Users_AuthorUserId"
        FOREIGN KEY ("AuthorUserId") REFERENCES "Users"("Id") ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS "IX_Subjects_AuthorUserId" ON "Subjects"("AuthorUserId");

-- ── CourseLesson.SourceSubjectId : lien "Ajouter à une formation" (US-CAT-03),
--    sert aussi à calculer "X enseignants ont utilisé ce contenu" (US-CAT-02).
ALTER TABLE "CourseLessons"
    ADD COLUMN IF NOT EXISTS "SourceSubjectId" INTEGER;

ALTER TABLE "CourseLessons"
    DROP CONSTRAINT IF EXISTS "FK_CourseLessons_Subjects_SourceSubjectId";

ALTER TABLE "CourseLessons"
    ADD CONSTRAINT "FK_CourseLessons_Subjects_SourceSubjectId"
        FOREIGN KEY ("SourceSubjectId") REFERENCES "Subjects"("Id") ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS "IX_CourseLessons_SourceSubjectId" ON "CourseLessons"("SourceSubjectId");

-- ── ConcoursEvents : conseils de réussite + FAQ (US-CAT-09) ────────────────
ALTER TABLE "ConcoursEvents"
    ADD COLUMN IF NOT EXISTS "Tips" VARCHAR(2000);

ALTER TABLE "ConcoursEvents"
    ADD COLUMN IF NOT EXISTS "FaqJson" TEXT;

-- ── TeacherClassContents : contenu assigné par un professeur à une classe
--    (US-CAT-04). Un seul débit par (classe, contenu) : les élèves de la
--    classe y accèdent ensuite sans payer individuellement.
CREATE TABLE IF NOT EXISTS "TeacherClassContents" (
    "Id"                SERIAL PRIMARY KEY,
    "TeacherClassId"    INTEGER NOT NULL,
    "SubjectId"         INTEGER NOT NULL,
    "AssignedByUserId"  INTEGER NOT NULL,
    "PriceChargedXaf"   NUMERIC(10,2) NOT NULL DEFAULT 0,
    "AssignedAt"        TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_TeacherClassContents_TeacherClasses_TeacherClassId"
        FOREIGN KEY ("TeacherClassId") REFERENCES "TeacherClasses"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TeacherClassContents_Subjects_SubjectId"
        FOREIGN KEY ("SubjectId") REFERENCES "Subjects"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TeacherClassContents_TeacherClassId_SubjectId"
    ON "TeacherClassContents"("TeacherClassId", "SubjectId");

-- ── Historique EF : voir SQL_AddTutorProfileModule.sql pour le raisonnement
--    (le reste du projet applique déjà ses évolutions par SQL manuel).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260906000000_AddCatalogPurchaseModule', '7.0.5')
ON CONFLICT DO NOTHING;
