-- Migration: AddAccessRequestStages
-- Hiérarchie à deux étapes pour les demandes d'accès enseignant->élève
-- (TeacherStudentAccessRequests) : remplace le modèle "premier qui répond
-- gagne" sans hiérarchie. Étape 1 = filtre administratif institution,
-- étape 2 = consentement élève/parent. Voir TeacherStudentAccessRequest.cs.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "TeacherStudentAccessRequests"
    ADD COLUMN IF NOT EXISTS "Stage" VARCHAR(20) NOT NULL DEFAULT 'institution',
    ADD COLUMN IF NOT EXISTS "InstitutionApprovedBy" INTEGER,
    ADD COLUMN IF NOT EXISTS "InstitutionRespondedAt" TIMESTAMP WITH TIME ZONE;

-- Les demandes déjà "accepted"/"rejected" avant cette migration ont, de fait,
-- franchi les deux étapes (l'ancien modèle ne distinguait pas) : les marquer
-- "consent" pour ne pas les faire réapparaître comme "en attente institution"
-- dans un filtre futur qui s'appuierait sur Stage.
UPDATE "TeacherStudentAccessRequests"
SET "Stage" = 'consent'
WHERE "Status" IN ('accepted', 'rejected');

CREATE INDEX IF NOT EXISTS "IX_TeacherStudentAccessRequests_Status_Stage"
    ON "TeacherStudentAccessRequests"("Status", "Stage");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260916100000_AddAccessRequestStages', '8.0.0')
ON CONFLICT DO NOTHING;
