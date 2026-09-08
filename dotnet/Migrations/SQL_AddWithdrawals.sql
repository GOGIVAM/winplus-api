-- Migration: AddWithdrawals
-- prompt_prof.md Module 7 — Revenus et paiements (retrait Mobile Money)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "Withdrawals" (
    "Id"          SERIAL PRIMARY KEY,
    "UserId"      INTEGER NOT NULL,
    "Operator"    VARCHAR(20) NOT NULL DEFAULT 'mtn',
    "Phone"       VARCHAR(30) NOT NULL,
    "AmountXaf"   NUMERIC(12,2) NOT NULL,
    "Status"      VARCHAR(20) NOT NULL DEFAULT 'pending',
    "AdminNote"   VARCHAR(300),
    "RequestedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "ProcessedAt" TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_Withdrawals_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_Withdrawals_UserId_Status" ON "Withdrawals"("UserId", "Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260908110000_AddWithdrawals', '8.0.0')
ON CONFLICT DO NOTHING;
