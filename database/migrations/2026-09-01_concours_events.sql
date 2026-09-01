-- Migration: ConcoursEvents table
-- Date: 2026-09-01

CREATE TABLE IF NOT EXISTS "ConcoursEvents" (
    "Id"                      SERIAL PRIMARY KEY,
    "Slug"                    VARCHAR(50)  NOT NULL,
    "Name"                    VARCHAR(200) NOT NULL,
    "Year"                    INTEGER      NOT NULL,
    "RegistrationStartDate"   TIMESTAMP WITH TIME ZONE,
    "RegistrationEndDate"     TIMESTAMP WITH TIME ZONE,
    "ExamDate"                TIMESTAMP WITH TIME ZONE,
    "ResultsDate"             TIMESTAMP WITH TIME ZONE,
    "Location"                VARCHAR(300),
    "EnrollmentFeeXaf"        INTEGER,
    "OfficialRegistrationUrl" VARCHAR(500),
    "Notes"                   VARCHAR(1000),
    "IsPublished"             BOOLEAN      NOT NULL DEFAULT TRUE,
    "CreatedAt"               TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"               TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS "IX_ConcoursEvents_Slug_Year" ON "ConcoursEvents" ("Slug", "Year");

-- Seed data for concours 2026
INSERT INTO "ConcoursEvents" ("Slug", "Name", "Year", "RegistrationStartDate", "RegistrationEndDate", "ExamDate", "ResultsDate", "Location", "EnrollmentFeeXaf", "OfficialRegistrationUrl", "Notes") VALUES
  ('ens',           'ENS Yaoundé',                         2026, '2026-01-15 00:00:00+00', '2026-03-31 23:59:59+00', '2026-06-15 07:00:00+00', '2026-09-01 00:00:00+00', 'Yaoundé', 15000, 'https://minesup.gov.cm', NULL),
  ('polytechnique', 'École Polytechnique de Yaoundé',      2026, '2026-02-01 00:00:00+00', '2026-04-15 23:59:59+00', '2026-07-01 07:00:00+00', '2026-09-15 00:00:00+00', 'Yaoundé', 20000, 'https://polytechnique.cm', NULL),
  ('enam',          'ENAM',                                2026, '2026-01-20 00:00:00+00', '2026-04-01 23:59:59+00', '2026-06-20 07:00:00+00', '2026-10-01 00:00:00+00', 'Yaoundé / Douala', 15000, 'https://enam.cm', NULL),
  ('fmsb',          'Faculté de Médecine et Sciences Biomédicales', 2026, '2026-03-01 00:00:00+00', '2026-05-31 23:59:59+00', '2026-08-10 07:00:00+00', '2026-11-01 00:00:00+00', 'Yaoundé', 25000, 'https://fmsb.cm', NULL),
  ('essec',         'ESSEC Douala',                        2026, '2026-02-15 00:00:00+00', '2026-04-30 23:59:59+00', '2026-07-15 07:00:00+00', '2026-10-15 00:00:00+00', 'Douala', 15000, 'https://essec.cm', NULL),
  ('enset',         'ENSET',                               2026, '2026-01-25 00:00:00+00', '2026-03-31 23:59:59+00', '2026-06-25 07:00:00+00', '2026-09-20 00:00:00+00', 'Yaoundé / Douala', 12000, 'https://enset.cm', NULL),
  ('bac',           'Baccalauréat Camerounais',            2026, NULL, NULL, '2026-06-01 07:00:00+00', '2026-08-20 00:00:00+00', 'National', NULL, 'https://obc.cm', 'Toutes séries'),
  ('bepc',          'Brevet d''Études du Premier Cycle',   2026, NULL, NULL, '2026-05-20 07:00:00+00', '2026-07-20 00:00:00+00', 'National', NULL, 'https://obc.cm', NULL),
  ('probatoire',    'Probatoire',                          2026, NULL, NULL, '2026-05-27 07:00:00+00', '2026-07-25 00:00:00+00', 'National', NULL, 'https://obc.cm', NULL)
ON CONFLICT DO NOTHING;
