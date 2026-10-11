-- Migration: AddTutorPricingGridAndSessionTopics
-- Lot 9.1 (suivi Partie 17, décisions du 2026-10-11) : agenda en tranches de
-- 30 min, grille tarifaire du répétiteur (17.3) et sujets de séance (17.5).
--   - TutorRates             : tarif de base par matière et par mode
--   - TutorRateModifiers     : modulation par créneau (jours / fenêtre horaire, +/- %)
--   - TutorDurationDiscounts : dégressivité selon la durée réservée
--   - TutorBookingTopics     : sujets à travailler saisis à la réservation
--   - TutorBookings.PriceBreakdownJson : devis serveur figé à la création
-- Equivalent EF entities: Models/Entities/TutorPricing.cs, TutorBooking.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
-- Additif uniquement : aucune colonne retirée ni renommée. Les réservations
-- existantes gardent leur PriceXaf figé (PriceBreakdownJson reste NULL), et
-- TutorProfiles.HourlyRateXaf reste le tarif de base tant que le répétiteur
-- n'a saisi aucune ligne dans TutorRates.

-- ── TutorRates : tarif de base par matière (NULL = toutes) et par mode ─────
CREATE TABLE IF NOT EXISTS "TutorRates" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER NOT NULL,
    "Subject"         VARCHAR(100),
    "Mode"            VARCHAR(20),
    "HourlyRateXaf"   NUMERIC(10,2) NOT NULL,
    CONSTRAINT "FK_TutorRates_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorRates_TutorProfileId" ON "TutorRates"("TutorProfileId");

-- ── TutorRateModifiers : majoration / minoration par créneau ───────────────
CREATE TABLE IF NOT EXISTS "TutorRateModifiers" (
    "Id"                 SERIAL PRIMARY KEY,
    "TutorProfileId"     INTEGER NOT NULL,
    "Label"              VARCHAR(60) NOT NULL,
    "DaysOfWeekMask"     INTEGER NOT NULL DEFAULT 0,
    "StartTime"          INTERVAL,
    "EndTime"            INTERVAL,
    "PercentAdjustment"  INTEGER NOT NULL,
    CONSTRAINT "FK_TutorRateModifiers_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorRateModifiers_TutorProfileId" ON "TutorRateModifiers"("TutorProfileId");

-- ── TutorDurationDiscounts : paliers de dégressivité ───────────────────────
CREATE TABLE IF NOT EXISTS "TutorDurationDiscounts" (
    "Id"                  SERIAL PRIMARY KEY,
    "TutorProfileId"      INTEGER NOT NULL,
    "MinDurationMinutes"  INTEGER NOT NULL,
    "PercentDiscount"     INTEGER NOT NULL,
    CONSTRAINT "FK_TutorDurationDiscounts_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorDurationDiscounts_TutorProfileId" ON "TutorDurationDiscounts"("TutorProfileId");

-- ── TutorBookingTopics : sujets de séance (17.5) ───────────────────────────
CREATE TABLE IF NOT EXISTS "TutorBookingTopics" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorBookingId"  INTEGER NOT NULL,
    "Label"           VARCHAR(100) NOT NULL,
    CONSTRAINT "FK_TutorBookingTopics_TutorBookings_TutorBookingId"
        FOREIGN KEY ("TutorBookingId") REFERENCES "TutorBookings"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorBookingTopics_TutorBookingId" ON "TutorBookingTopics"("TutorBookingId");

-- ── TutorBookings : devis figé ────────────────────────────────────────────
ALTER TABLE "TutorBookings" ADD COLUMN IF NOT EXISTS "PriceBreakdownJson" TEXT NULL;

-- Historique EF : évite que `dotnet ef migrations` s'y perde si l'équipe
-- exécute un jour les migrations normalement (le reste du projet applique
-- déjà ses évolutions par SQL manuel, voir les autres fichiers SQL_*.sql).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261011120000_AddTutorPricingGridAndSessionTopics', '8.0.0')
ON CONFLICT DO NOTHING;
