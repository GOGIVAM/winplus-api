-- Migration: AddTutorBookingModule
-- Module 6 — Réservation de séances de cours particulier (professeur_complete.md)
-- Equivalent EF entity: Models/Entities/TutorBooking.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "TutorBookings" (
    "Id"                   SERIAL PRIMARY KEY,
    "TutorProfileId"       INTEGER NOT NULL,
    "StudentUserId"        INTEGER NOT NULL,
    "SessionDate"          DATE NOT NULL,
    "StartTime"            INTERVAL NOT NULL,
    "EndTime"              INTERVAL NOT NULL,
    "Mode"                 VARCHAR(30) NOT NULL DEFAULT 'online',
    "PriceXaf"             NUMERIC(10,2) NOT NULL DEFAULT 0,
    "Status"               VARCHAR(20) NOT NULL DEFAULT 'pending_payment',
    "NotchpayReference"    VARCHAR(100),
    "PaymentStatus"        VARCHAR(20) NOT NULL DEFAULT 'pending',
    "PhoneNumber"          VARCHAR(30),
    "CancellationReason"   VARCHAR(300),
    "CancelledByUserId"    INTEGER,
    "CancelledAt"          TIMESTAMP WITH TIME ZONE,
    "ConfirmedAt"          TIMESTAMP WITH TIME ZONE,
    "CreatedAt"            TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"            TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_TutorBookings_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorBookings_Users_StudentUserId"
        FOREIGN KEY ("StudentUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorBookings_NotchpayReference" ON "TutorBookings"("NotchpayReference");
CREATE INDEX IF NOT EXISTS "IX_TutorBookings_TutorProfileId_SessionDate" ON "TutorBookings"("TutorProfileId", "SessionDate");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907120000_AddTutorBookingModule', '7.0.5')
ON CONFLICT DO NOTHING;
