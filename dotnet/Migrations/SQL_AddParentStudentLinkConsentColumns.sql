-- Migration: AddParentStudentLinkConsentColumns
-- L'entité ParentStudentLink (Models/Entities/ParentStudentLink.cs) a été
-- étendue avec Status/InitiatedBy/UpdatedAt pour exiger le consentement de
-- l'élève avant de lier un parent (au lieu de la liaison instantanée
-- d'origine), mais la migration EF d'origine (20260624_AddParentStudentLinks)
-- n'a jamais été suivie d'une migration pour ces colonnes — la table réelle
-- n'avait donc que Id/ParentId/StudentId/CreatedAt, d'où l'erreur Postgres
-- 42703 "column p.InitiatedBy does not exist" dès qu'une requête EF (ex.
-- AdminUsersController.ListStudents) matérialisait l'entité complète.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "ParentStudentLinks" ADD COLUMN IF NOT EXISTS "Status" VARCHAR(20) NOT NULL DEFAULT 'accepted';
ALTER TABLE "ParentStudentLinks" ADD COLUMN IF NOT EXISTS "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW();
ALTER TABLE "ParentStudentLinks" ADD COLUMN IF NOT EXISTS "InitiatedBy" INTEGER;

-- Backfill des lignes déjà existantes (créées avant ce correctif, donc déjà
-- effectives) : Status='accepted' par le DEFAULT ci-dessus préserve l'accès
-- déjà accordé sans le faire dépendre d'un consentement rétroactif ;
-- InitiatedBy=ParentId reflète le comportement d'alors ("AddChild créait la
-- ligne directement", donc le parent était de fait l'initiateur).
UPDATE "ParentStudentLinks" SET "InitiatedBy" = "ParentId" WHERE "InitiatedBy" IS NULL;

ALTER TABLE "ParentStudentLinks" ALTER COLUMN "InitiatedBy" SET NOT NULL;

ALTER TABLE "ParentStudentLinks"
    DROP CONSTRAINT IF EXISTS "FK_ParentStudentLinks_Users_InitiatedBy";
ALTER TABLE "ParentStudentLinks"
    ADD CONSTRAINT "FK_ParentStudentLinks_Users_InitiatedBy"
    FOREIGN KEY ("InitiatedBy") REFERENCES "Users"("Id") ON DELETE RESTRICT;

CREATE INDEX IF NOT EXISTS "IX_ParentStudentLinks_InitiatedBy" ON "ParentStudentLinks"("InitiatedBy");
CREATE INDEX IF NOT EXISTS "IX_ParentStudentLinks_Status" ON "ParentStudentLinks"("Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260909000000_AddParentStudentLinkConsentColumns', '8.0.0')
ON CONFLICT DO NOTHING;
