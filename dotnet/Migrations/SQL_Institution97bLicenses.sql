-- Migration: Institution97bLicenses
-- Lot 9.7b, étape 4 (étude 9.7b section 2.3, suivi 18.11 et 18.21) : licences
-- d'utilisation des établissements, par siège élève, sur l'année scolaire ou
-- un trimestre, avec sièges enseignants (un pour 15 élèves, 3 au minimum).
--   * InstitutionLicenses : palier, statut, période, sièges, prix figé, budget
--     WinAI de l'établissement, renouvellement par le portefeuille, grâce ;
--   * InstitutionSeats : siège nominatif d'un élève (rattachement accepté) ou
--     d'un enseignant (affiliation acceptée), attribution et libération.
-- Equivalent EF : Models/Entities/InstitutionLicense.cs,
-- Data/ApplicationDbContext.Subscriptions97b.cs.
--
-- Prérequis : la table historique "Institutions". Indépendant de l'ordre des
-- autres scripts SQL_*.sql. Idempotent. Additif : aucune donnée modifiée.
-- À exécuter AVANT le déploiement du code.
-- Longueurs : Status VARCHAR(20) (« trialing », « past_due », « cancelled » ≤ 9),
-- Tier VARCHAR(20) (« establishment » = 13, « negotiated » = 10),
-- Term VARCHAR(20) (« quarter » = 7), Kind VARCHAR(20) (« teacher » = 7).

CREATE TABLE IF NOT EXISTS "InstitutionLicenses" (
    "Id"                  SERIAL        PRIMARY KEY,
    "InstitutionId"       INTEGER       NOT NULL REFERENCES "Institutions"("Id") ON DELETE RESTRICT,
    "Tier"                VARCHAR(20)   NOT NULL DEFAULT 'school',
    "Status"              VARCHAR(20)   NOT NULL DEFAULT 'active',
    "Term"                VARCHAR(20)   NOT NULL DEFAULT 'year',
    "StartDate"           TIMESTAMP WITH TIME ZONE NOT NULL,
    "EndDate"             TIMESTAMP WITH TIME ZONE NOT NULL,
    "GraceUntil"          TIMESTAMP WITH TIME ZONE NULL,
    "StudentSeats"        INTEGER       NOT NULL,
    "TeacherSeats"        INTEGER       NOT NULL,
    "PricePerSeatTtc"     NUMERIC(12,0) NOT NULL DEFAULT 0,
    "TotalPriceTtc"       NUMERIC(14,0) NOT NULL DEFAULT 0,
    "IsNegotiated"        BOOLEAN       NOT NULL DEFAULT FALSE,
    "AutoRenew"           BOOLEAN       NOT NULL DEFAULT FALSE,
    "CancelAtPeriodEnd"   BOOLEAN       NOT NULL DEFAULT FALSE,
    "AiBudgetTokens"      BIGINT        NOT NULL DEFAULT 0,
    "PromoCodeId"         INTEGER       NULL,
    "CreatedByUserId"     INTEGER       NULL,
    "Notes"               VARCHAR(1000) NULL,
    "RenewalNoticeSentAt" TIMESTAMP WITH TIME ZONE NULL,
    "CreatedAt"           TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"           TIMESTAMP WITH TIME ZONE NULL
);
CREATE INDEX IF NOT EXISTS "IX_InstitutionLicenses_InstitutionId_Status" ON "InstitutionLicenses"("InstitutionId", "Status");
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_InstitutionLicenses_Values') THEN
        ALTER TABLE "InstitutionLicenses" ADD CONSTRAINT "CK_InstitutionLicenses_Values" CHECK (
            "Status" IN ('trialing', 'active', 'past_due', 'expired', 'cancelled')
            AND "Tier" IN ('school', 'establishment', 'network', 'negotiated', 'trial')
            AND "Term" IN ('year', 'quarter')
            AND "StudentSeats" > 0 AND "TeacherSeats" >= 0 AND "EndDate" > "StartDate" - INTERVAL '1 second');
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "InstitutionSeats" (
    "Id"               SERIAL      PRIMARY KEY,
    "LicenseId"        INTEGER     NOT NULL REFERENCES "InstitutionLicenses"("Id") ON DELETE CASCADE,
    "Kind"             VARCHAR(20) NOT NULL DEFAULT 'student',
    "UserId"           INTEGER     NOT NULL,
    "AssignedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "ReleasedAt"       TIMESTAMP WITH TIME ZONE NULL,
    "AssignedByUserId" INTEGER     NULL
);
CREATE INDEX IF NOT EXISTS "IX_InstitutionSeats_LicenseId_Kind" ON "InstitutionSeats"("LicenseId", "Kind");
CREATE INDEX IF NOT EXISTS "IX_InstitutionSeats_UserId_Kind" ON "InstitutionSeats"("UserId", "Kind");
-- Un même compte n'occupe qu'un siège actif d'un type donné par licence.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_InstitutionSeats_Active" ON "InstitutionSeats"("LicenseId", "Kind", "UserId") WHERE "ReleasedAt" IS NULL;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_InstitutionSeats_Kind') THEN
        ALTER TABLE "InstitutionSeats" ADD CONSTRAINT "CK_InstitutionSeats_Kind" CHECK ("Kind" IN ('student', 'teacher'));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261016120000_Institution97bLicenses', '8.0.0')
ON CONFLICT DO NOTHING;
