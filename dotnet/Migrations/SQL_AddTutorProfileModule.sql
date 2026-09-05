-- Migration: AddTutorProfileModule
-- Module 1 — Onboarding et profil répétiteur (professeur_complete.md)
-- Equivalent EF entities: Models/Entities/TutorProfile.cs
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

-- ── TutorProfiles : un profil "Mode Répétiteur" par utilisateur ────────────
CREATE TABLE IF NOT EXISTS "TutorProfiles" (
    "Id"                     SERIAL PRIMARY KEY,
    "UserId"                 INTEGER NOT NULL,
    "Title"                  VARCHAR(150),
    "TutorBio"               VARCHAR(300),
    "VideoUrl"               VARCHAR(500),
    "TeachingStyle"          VARCHAR(30),
    "HourlyRateXaf"          NUMERIC(10,2),
    "TrialSessionEnabled"    BOOLEAN NOT NULL DEFAULT FALSE,
    "TrialSessionPriceXaf"   NUMERIC(10,2),
    "OffersAtStudentHome"    BOOLEAN NOT NULL DEFAULT FALSE,
    "OffersAtTutorHome"      BOOLEAN NOT NULL DEFAULT FALSE,
    "OffersOnline"           BOOLEAN NOT NULL DEFAULT FALSE,
    "OffersNeutralPlace"     BOOLEAN NOT NULL DEFAULT FALSE,
    "TutorHomeAddressHint"   VARCHAR(200),
    "NoticeHours"            INTEGER NOT NULL DEFAULT 24,
    "MaxSessionsPerWeek"     INTEGER,
    "IsOnVacation"           BOOLEAN NOT NULL DEFAULT FALSE,
    "IsDiplomaVerified"      BOOLEAN NOT NULL DEFAULT FALSE,
    "IsActive"               BOOLEAN NOT NULL DEFAULT FALSE,
    "OnboardingStep"         INTEGER NOT NULL DEFAULT 0,
    "CreatedAt"              TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"              TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_TutorProfiles_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorProfiles_UserId" ON "TutorProfiles"("UserId");

-- ── TutorSubjects : matières enseignées (multiselect, US-PRO-02) ───────────
CREATE TABLE IF NOT EXISTS "TutorSubjects" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER NOT NULL,
    "Subject"         VARCHAR(100) NOT NULL,
    CONSTRAINT "FK_TutorSubjects_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorSubjects_TutorProfileId_Subject"
    ON "TutorSubjects"("TutorProfileId", "Subject");

-- ── TutorLevels : niveaux couverts (6ème à Tle, concours...) ───────────────
CREATE TABLE IF NOT EXISTS "TutorLevels" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER NOT NULL,
    "Level"           VARCHAR(100) NOT NULL,
    CONSTRAINT "FK_TutorLevels_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TutorLevels_TutorProfileId_Level"
    ON "TutorLevels"("TutorProfileId", "Level");

-- ── TutorSpecialties : tags libres ("Préparation concours"...) ────────────
CREATE TABLE IF NOT EXISTS "TutorSpecialties" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER NOT NULL,
    "Label"           VARCHAR(100) NOT NULL,
    CONSTRAINT "FK_TutorSpecialties_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorSpecialties_TutorProfileId" ON "TutorSpecialties"("TutorProfileId");

-- ── TutorInterventionZones : zones géographiques (à domicile) ──────────────
CREATE TABLE IF NOT EXISTS "TutorInterventionZones" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER NOT NULL,
    "City"            VARCHAR(100) NOT NULL,
    "Quartier"        VARCHAR(100),
    CONSTRAINT "FK_TutorInterventionZones_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorInterventionZones_TutorProfileId" ON "TutorInterventionZones"("TutorProfileId");

-- ── TutorPackages : forfaits multi-séances (US-PRO-10) ─────────────────────
CREATE TABLE IF NOT EXISTS "TutorPackages" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER NOT NULL,
    "Name"            VARCHAR(100) NOT NULL,
    "SessionsCount"   INTEGER NOT NULL,
    "TotalPriceXaf"   NUMERIC(10,2) NOT NULL,
    "IsActive"        BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT "FK_TutorPackages_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorPackages_TutorProfileId" ON "TutorPackages"("TutorProfileId");

-- ── TutorAvailabilitySlots : grille hebdomadaire type (US-PRO-08) ──────────
CREATE TABLE IF NOT EXISTS "TutorAvailabilitySlots" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER NOT NULL,
    "DayOfWeek"       INTEGER NOT NULL,
    "StartTime"       INTERVAL NOT NULL,
    "EndTime"         INTERVAL NOT NULL,
    "IsActive"        BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT "FK_TutorAvailabilitySlots_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorAvailabilitySlots_TutorProfileId" ON "TutorAvailabilitySlots"("TutorProfileId");

-- ── TutorVerificationDocuments : diplôme → badge "Vérifié Diplôme" (US-PRO-03)
CREATE TABLE IF NOT EXISTS "TutorVerificationDocuments" (
    "Id"                  SERIAL PRIMARY KEY,
    "TutorProfileId"      INTEGER NOT NULL,
    "DocumentUrl"         VARCHAR(500) NOT NULL,
    "Status"              VARCHAR(20) NOT NULL DEFAULT 'pending',
    "RejectionReason"     VARCHAR(500),
    "SubmittedAt"         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "ReviewedAt"          TIMESTAMP WITH TIME ZONE,
    "ReviewedByUserId"    INTEGER,
    CONSTRAINT "FK_TutorVerificationDocuments_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_TutorVerificationDocuments_TutorProfileId" ON "TutorVerificationDocuments"("TutorProfileId");

-- ── Historique EF : évite que `dotnet ef migrations` s'y perde si l'équipe
--    exécute un jour les migrations normalement (le reste du projet applique
--    déjà ses évolutions par SQL manuel, voir les autres fichiers SQL_*.sql).
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260905201319_AddTutorProfileModule', '7.0.5')
ON CONFLICT DO NOTHING;
