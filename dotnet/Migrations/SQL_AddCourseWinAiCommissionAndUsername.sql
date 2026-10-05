-- Migration: AddCourseWinAiCommissionAndUsername
-- Module 6/7 : une formation (Course) relève du même mécanisme de commission
-- WinAI que le catalogue (Module 7, §7B), en prévision du jour où l'achat
-- d'une formation sera réellement câblé au panier (voir le rapport de lot :
-- CartItem/OrderItem n'ont aujourd'hui aucun chemin réel pour CourseId).
-- Module 35 : nom d'utilisateur pour l'affichage anonymisé du classement de
-- formation (le professeur continue de voir les vrais noms). Nullable et
-- sans valeur générée automatiquement : un champ encore vide n'anonymise
-- pas moins (voir CourseGamificationService, repli sur "Élève #ID").
-- Equivalent EF entities: Models/Entities/Course.cs, Models/Entities/User.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "WinAiScore" NUMERIC(5,2);
ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "WinAiJustification" TEXT;
ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "PlatformCommissionRate" NUMERIC(5,4);
ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "WinAiScoreEvaluatedAt" TIMESTAMP WITH TIME ZONE;

-- Module 6/31 : examen final distinct, obligatoire pour le certificat quand
-- il est configuré (voir Services/CourseCertificateService.cs). Réutilise
-- Quizzes existant : FinalExamQuizId n'est qu'une référence, pas une
-- nouvelle structure d'examen.
ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "FinalExamQuizId" INTEGER NULL;
ALTER TABLE "Courses" ADD COLUMN IF NOT EXISTS "FinalExamPassScorePercent" INTEGER NOT NULL DEFAULT 50;

ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Username" VARCHAR(32);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username") WHERE "Username" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005120200_AddCourseWinAiCommissionAndUsername', '8.0.0')
ON CONFLICT DO NOTHING;
