-- Migration: AddSessionsModule
-- Module 5 — Sessions (professeur_complete.md)
-- Equivalent EF entities: Session.cs (redesign), SessionEnrollment.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "Type" VARCHAR(20) NOT NULL DEFAULT 'live';
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "Subject" VARCHAR(100);
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "Level" VARCHAR(100);
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "DurationMinutes" INTEGER NOT NULL DEFAULT 60;
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "IsFree" BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "PriceXaf" NUMERIC(10,2);
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "ExternalLink" VARCHAR(500);
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "CancelledAt" TIMESTAMP WITH TIME ZONE;
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "CancelledByUserId" INTEGER;
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "CancellationReason" VARCHAR(500);
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "TranscriptText" TEXT;
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "SummaryText" TEXT;
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "UpdatedAt" TIMESTAMP WITH TIME ZONE;
ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "IsDeleted" BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS "IX_Sessions_CreatedBy" ON "Sessions"("CreatedBy");
CREATE INDEX IF NOT EXISTS "IX_Sessions_StartDate" ON "Sessions"("StartDate");
CREATE INDEX IF NOT EXISTS "IX_Sessions_IsDeleted" ON "Sessions"("IsDeleted");

-- ── SessionEnrollments : inscriptions élèves à une session ──────────────────
CREATE TABLE IF NOT EXISTS "SessionEnrollments" (
    "Id"                 SERIAL PRIMARY KEY,
    "SessionId"          INTEGER NOT NULL,
    "StudentId"          INTEGER NOT NULL,
    "EnrolledAt"         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "PaymentStatus"      VARCHAR(20) NOT NULL DEFAULT 'free',
    "PriceChargedXaf"    NUMERIC(10,2),
    "NotchpayReference"  VARCHAR(255),
    CONSTRAINT "FK_SessionEnrollments_Sessions_SessionId"
        FOREIGN KEY ("SessionId") REFERENCES "Sessions"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SessionEnrollments_Users_StudentId"
        FOREIGN KEY ("StudentId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SessionEnrollments_SessionId_StudentId"
    ON "SessionEnrollments"("SessionId", "StudentId");

-- ── Historique EF : voir SQL_AddTutorProfileModule.sql pour le raisonnement.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260906180000_AddSessionsModule', '8.0.0')
ON CONFLICT DO NOTHING;
