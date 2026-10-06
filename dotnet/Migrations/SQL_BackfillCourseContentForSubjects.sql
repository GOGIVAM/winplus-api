-- Migration: BackfillCourseContentForSubjects
-- Rattrapage ponctuel du correctif "Mes contenus" (commit 9dc58fe) :
-- SubjectsController.Create crée désormais un CourseContent lié à chaque
-- Subject publié par un enseignant/admin via l'upload (Module 8), mais ce
-- correctif ne s'applique qu'aux Subjects créés APRÈS son déploiement. Tout
-- Subject avec un AuthorUserId déjà en base avant cette date reste invisible
-- dans "Mes contenus" (TeacherContentController.GetMine), qui ne lit que
-- CourseContents.CreatedByUserId.
--
-- Idempotent : n'insère que pour les Subjects qui n'ont encore AUCUN
-- CourseContent (peut être rejoué sans dupliquer).

INSERT INTO "CourseContents" ("SubjectId", "Title", "Description", "DocumentUrl", "CreatedByUserId", "Status", "OrderIndex", "DurationMinutes", "IsLocked", "CreatedAt")
SELECT
    s."Id",
    s."Title",
    s."Description",
    s."DocumentUrl",
    s."AuthorUserId",
    CASE WHEN s."IsPublished" THEN 'published' ELSE 'review' END,
    0,
    0,
    FALSE,
    s."CreatedAt"
FROM "Subjects" s
WHERE s."AuthorUserId" IS NOT NULL
  AND s."IsDeleted" = FALSE
  AND NOT EXISTS (
    SELECT 1 FROM "CourseContents" cc WHERE cc."SubjectId" = s."Id"
  );
