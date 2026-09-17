-- Migration: AddGoalProposalWorkflow
-- Goal (Models/Entities/Goal.cs) n'avait aucun endpoint de création, même côté
-- élève, et un Status simplifié ("active"/"completed"/"cancelled", sans
-- workflow de proposition). Ajoute ProposedByUserId (qui a proposé l'objectif,
-- null si créé directement par l'élève) et étend Status pour couvrir le
-- workflow parent → élève : Pending/Accepted/Refused/Active/Completed/Cancelled
-- (voir GoalStatus, GoalsController). Idempotent : peut être rejoué sans
-- casser une base déjà migrée.

ALTER TABLE "Goals" ADD COLUMN IF NOT EXISTS "ProposedByUserId" INTEGER NULL;

ALTER TABLE "Goals" DROP CONSTRAINT IF EXISTS "FK_Goals_Users_ProposedByUserId";
ALTER TABLE "Goals"
    ADD CONSTRAINT "FK_Goals_Users_ProposedByUserId"
    FOREIGN KEY ("ProposedByUserId") REFERENCES "Users"("Id") ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS "IX_Goals_ProposedByUserId" ON "Goals"("ProposedByUserId");
CREATE INDEX IF NOT EXISTS "IX_Goals_Status" ON "Goals"("Status");

-- Backfill : toutes les lignes existantes ont été créées avant ce workflow,
-- donc sans proposition parent ni statut Pending/Refused à préserver. On
-- recase juste la casse/les valeurs vers le nouveau vocabulaire : "completed"
-- et "cancelled" gardent leur sens (recasés), tout le reste (dont "active",
-- "in_progress" et NULL) devient Active — "données en base = déjà actifs".
UPDATE "Goals" SET "Status" = 'Completed' WHERE LOWER("Status") = 'completed' AND "Status" <> 'Completed';
UPDATE "Goals" SET "Status" = 'Cancelled' WHERE LOWER("Status") = 'cancelled' AND "Status" <> 'Cancelled';
UPDATE "Goals" SET "Status" = 'Active' WHERE "Status" IS NULL OR "Status" NOT IN ('Completed', 'Cancelled');

ALTER TABLE "Goals" ALTER COLUMN "Status" SET DEFAULT 'Active';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260916000000_AddGoalProposalWorkflow', '8.0.0')
ON CONFLICT DO NOTHING;
