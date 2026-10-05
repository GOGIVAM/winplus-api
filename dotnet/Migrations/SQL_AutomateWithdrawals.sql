-- Migration: AutomateWithdrawals
-- Module 2 (lot 2) : virement automatisé NotchPay, décision §15 du suivi.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Ordre : après SQL_AddWithdrawals.sql et SQL_AddWalletLedger.sql, avant le code.

ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "TransferReference"   VARCHAR(60);
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "ProviderTransferId"  VARCHAR(80);
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "FailureReason"       VARCHAR(300);
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "LastSyncedAt"        TIMESTAMP WITH TIME ZONE;
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "ClientRequestId"     VARCHAR(64);
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "RetryOfWithdrawalId" INTEGER;
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "ClosedAt"            TIMESTAMP WITH TIME ZONE;
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "ClosedByUserId"      INTEGER;
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "ActionByUserId"      INTEGER;
ALTER TABLE "Withdrawals" ADD COLUMN IF NOT EXISTS "Anomaly"             VARCHAR(300);

-- Le registre comptable ne suit pas le cycle de vie du compte : la
-- suppression d'un utilisateur ne doit plus effacer ses retraits (la clé
-- étrangère d'origine était en ON DELETE CASCADE).
ALTER TABLE "Withdrawals" DROP CONSTRAINT IF EXISTS "FK_Withdrawals_Users_UserId";
ALTER TABLE "Withdrawals" ADD CONSTRAINT "FK_Withdrawals_Users_UserId"
    FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE RESTRICT;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Withdrawals_TransferReference"
    ON "Withdrawals" ("TransferReference") WHERE "TransferReference" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Withdrawals_UserId_ClientRequestId"
    ON "Withdrawals" ("UserId", "ClientRequestId") WHERE "ClientRequestId" IS NOT NULL;
CREATE INDEX IF NOT EXISTS "IX_Withdrawals_Status_RequestedAt"
    ON "Withdrawals" ("Status", "RequestedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261003110000_AutomateWithdrawals', '8.0.0')
ON CONFLICT DO NOTHING;
