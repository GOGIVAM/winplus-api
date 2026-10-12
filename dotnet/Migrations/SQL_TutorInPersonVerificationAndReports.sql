-- Migration: TutorInPersonVerificationAndReports
-- Lot 9.5 (suivi Partie 17.11, 17.12 et 17.16, décisions du 2026-10-11 et du 2026-10-12) :
--   * présentiel réservé aux tuteurs vérifiés, par l'une de trois voies :
--     pièce d'identité + selfie validés par un admin, entretien réussi (avec
--     pièce d'identité + selfie validés), ou caution d'une institution de
--     rattachement (révocable) ;
--   * notes par les participants et le payeur, un seul avis par séance et par personne ;
--   * signalements, alerte admin, suspension automatique du présentiel au seuil.
-- Equivalent EF : Models/Entities/TutorVerification.cs, TutorProfile.cs
-- (InPersonVerifiedAt, InPersonVerificationRoutes, InPersonSuspended*),
-- TutorReview.cs (ReviewerRole, index unique séance + auteur).
--
-- Idempotent : rejouable sans effet sur une base déjà migrée.
-- Additif : aucune colonne retirée ni renommée, aucune donnée modifiée. Seul
-- l'index unique « IX_TutorReviews_TutorBookingId » (un avis par séance) est
-- remplacé par un index unique séance + auteur, plus permissif : toutes les
-- lignes existantes le respectent.
-- Indépendant de l'ordre alphabétique : ne lit ni ne modifie que des tables du
-- module répétiteur créées bien avant (TutorProfiles, TutorBookings,
-- TutorReviews, Institutions, Users) et les objets qu'il crée lui-même.
--
-- Après ce script, AUCUN tuteur n'est vérifié pour le présentiel (colonnes
-- nulles) : le serveur refuse les nouvelles réservations présentielles tant
-- qu'une voie n'est pas validée. Les réservations présentielles déjà prises ne
-- sont pas annulées ; le nombre de celles encore à venir est affiché en fin de
-- script (NOTICE) et l'écran admin « Vérification » le montre aussi.

-- ── TutorProfiles : statut calculé et suspension ─────────────────────────
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "InPersonVerifiedAt"          TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "InPersonVerificationRoutes"  VARCHAR(60)  NULL;
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "InPersonSuspendedAt"         TIMESTAMP WITH TIME ZONE NULL;
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "InPersonSuspensionReason"    VARCHAR(300) NULL;
ALTER TABLE "TutorProfiles" ADD COLUMN IF NOT EXISTS "InPersonSuspensionLiftedAt"  TIMESTAMP WITH TIME ZONE NULL;

-- ── Pièce d'identité + selfie (fichiers en stockage privé, références seules) ──
CREATE TABLE IF NOT EXISTS "TutorIdentityChecks" (
    "Id"                           SERIAL PRIMARY KEY,
    "TutorProfileId"               INTEGER      NOT NULL,
    "Source"                       VARCHAR(20)  NOT NULL DEFAULT 'tutor',
    "TutorVerificationInterviewId" INTEGER      NULL,
    "DocumentType"                 VARCHAR(30)  NOT NULL DEFAULT 'cni',
    "FrontFileRef"                 VARCHAR(500) NULL,
    "BackFileRef"                  VARCHAR(500) NULL,
    "SelfieFileRef"                VARCHAR(500) NULL,
    -- pending | approved | rejected | superseded (10 caractères au plus)
    "Status"                       VARCHAR(20)  NOT NULL DEFAULT 'pending',
    "RejectionReason"              VARCHAR(500) NULL,
    "SubmittedByUserId"            INTEGER      NOT NULL,
    "SubmittedAt"                  TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "ReviewedByUserId"             INTEGER      NULL,
    "ReviewedAt"                   TIMESTAMP WITH TIME ZONE NULL,
    "FilesPurgedAt"                TIMESTAMP WITH TIME ZONE NULL,
    CONSTRAINT "FK_TutorIdentityChecks_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_TutorIdentityChecks_TutorProfileId_Status" ON "TutorIdentityChecks"("TutorProfileId", "Status");
CREATE INDEX IF NOT EXISTS "IX_TutorIdentityChecks_Status_SubmittedAt"   ON "TutorIdentityChecks"("Status", "SubmittedAt");
ALTER TABLE "TutorIdentityChecks" DROP CONSTRAINT IF EXISTS "CK_TutorIdentityChecks_Status";
ALTER TABLE "TutorIdentityChecks" ADD CONSTRAINT "CK_TutorIdentityChecks_Status"
    CHECK ("Status" IN ('pending', 'approved', 'rejected', 'superseded'));

-- ── Entretien de vérification (visio ou rencontre), assisté par WinAI ────
CREATE TABLE IF NOT EXISTS "TutorVerificationInterviews" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER      NOT NULL,
    "Format"          VARCHAR(20)  NOT NULL DEFAULT 'video',
    "Availability"    VARCHAR(500) NULL,
    "City"            VARCHAR(100) NULL,
    "TutorMessage"    VARCHAR(500) NULL,
    -- requested | scheduled | passed | failed | no_show | cancelled (9 caractères au plus)
    "Status"          VARCHAR(20)  NOT NULL DEFAULT 'requested',
    "ScheduledAt"     TIMESTAMP WITH TIME ZONE NULL,
    "MeetingUrl"      VARCHAR(500) NULL,
    "Location"        VARCHAR(300) NULL,
    "AgentUserId"     INTEGER      NULL,
    "GuideJson"       TEXT         NULL,
    "AgentNotes"      TEXT         NULL,
    "ScoresJson"      TEXT         NULL,
    "ReportDraft"     TEXT         NULL,
    "FinalReport"     TEXT         NULL,
    "OutcomeReason"   VARCHAR(500) NULL,
    "RequestedAt"     TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CompletedAt"     TIMESTAMP WITH TIME ZONE NULL,
    "UpdatedAt"       TIMESTAMP WITH TIME ZONE NULL,
    CONSTRAINT "FK_TutorVerificationInterviews_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_TutorVerificationInterviews_TutorProfileId_Status" ON "TutorVerificationInterviews"("TutorProfileId", "Status");
CREATE INDEX IF NOT EXISTS "IX_TutorVerificationInterviews_Status_RequestedAt"   ON "TutorVerificationInterviews"("Status", "RequestedAt");
ALTER TABLE "TutorVerificationInterviews" DROP CONSTRAINT IF EXISTS "CK_TutorVerificationInterviews_Status";
ALTER TABLE "TutorVerificationInterviews" ADD CONSTRAINT "CK_TutorVerificationInterviews_Status"
    CHECK ("Status" IN ('requested', 'scheduled', 'passed', 'failed', 'no_show', 'cancelled'));
ALTER TABLE "TutorVerificationInterviews" DROP CONSTRAINT IF EXISTS "CK_TutorVerificationInterviews_Format";
ALTER TABLE "TutorVerificationInterviews" ADD CONSTRAINT "CK_TutorVerificationInterviews_Format"
    CHECK ("Format" IN ('video', 'in_person'));

-- ── Caution d'une institution de rattachement ─────────────────────────────
CREATE TABLE IF NOT EXISTS "TutorInstitutionEndorsements" (
    "Id"                SERIAL PRIMARY KEY,
    "TutorProfileId"    INTEGER      NOT NULL,
    "InstitutionId"     INTEGER      NOT NULL,
    "EndorsedByUserId"  INTEGER      NOT NULL,
    "Statement"         VARCHAR(500) NOT NULL DEFAULT '',
    "VerifierName"      VARCHAR(150) NOT NULL DEFAULT '',
    "VerifiedOn"        DATE         NOT NULL,
    -- active | revoked
    "Status"            VARCHAR(20)  NOT NULL DEFAULT 'active',
    "CreatedAt"         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "RevokedAt"         TIMESTAMP WITH TIME ZONE NULL,
    "RevokedByUserId"   INTEGER      NULL,
    "RevocationReason"  VARCHAR(300) NULL,
    CONSTRAINT "FK_TutorInstitutionEndorsements_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorInstitutionEndorsements_Institutions_InstitutionId"
        FOREIGN KEY ("InstitutionId") REFERENCES "Institutions"("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_TutorInstitutionEndorsements_TutorProfileId_Status" ON "TutorInstitutionEndorsements"("TutorProfileId", "Status");
CREATE INDEX IF NOT EXISTS "IX_TutorInstitutionEndorsements_InstitutionId"         ON "TutorInstitutionEndorsements"("InstitutionId");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_TutorInstitutionEndorsements_Active"
    ON "TutorInstitutionEndorsements"("TutorProfileId", "InstitutionId") WHERE "Status" = 'active';
ALTER TABLE "TutorInstitutionEndorsements" DROP CONSTRAINT IF EXISTS "CK_TutorInstitutionEndorsements_Status";
ALTER TABLE "TutorInstitutionEndorsements" ADD CONSTRAINT "CK_TutorInstitutionEndorsements_Status"
    CHECK ("Status" IN ('active', 'revoked'));

-- ── Historique (qui a validé quoi, quand, motif) ──────────────────────────
CREATE TABLE IF NOT EXISTS "TutorVerificationEvents" (
    "Id"              SERIAL PRIMARY KEY,
    "TutorProfileId"  INTEGER      NOT NULL,
    "Category"        VARCHAR(20)  NOT NULL,
    "Action"          VARCHAR(30)  NOT NULL,
    "ActorUserId"     INTEGER      NULL,
    "ActorRole"       VARCHAR(20)  NOT NULL DEFAULT 'system',
    "Reason"          VARCHAR(500) NULL,
    "RelatedId"       INTEGER      NULL,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS "IX_TutorVerificationEvents_TutorProfileId_CreatedAt" ON "TutorVerificationEvents"("TutorProfileId", "CreatedAt");

-- ── Signalements ──────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS "TutorReports" (
    "Id"                SERIAL PRIMARY KEY,
    "TutorProfileId"    INTEGER       NOT NULL,
    "TutorBookingId"    INTEGER       NOT NULL,
    "ReporterUserId"    INTEGER       NOT NULL,
    "ReporterRole"      VARCHAR(20)   NOT NULL DEFAULT 'participant',
    -- « inappropriate_behavior » (22 caractères) est le plus long des motifs.
    "Reason"            VARCHAR(40)   NOT NULL DEFAULT 'other',
    "Description"       VARCHAR(1000) NULL,
    -- « pending_tutor_approval » (22 caractères) : même largeur que TutorBookings.Status.
    "BookingStatus"     VARCHAR(30)   NOT NULL DEFAULT '',
    "Status"            VARCHAR(20)   NOT NULL DEFAULT 'open',
    "CreatedAt"         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "ReviewedAt"        TIMESTAMP WITH TIME ZONE NULL,
    "ReviewedByUserId"  INTEGER       NULL,
    "AdminNote"         VARCHAR(500)  NULL,
    CONSTRAINT "FK_TutorReports_TutorProfiles_TutorProfileId"
        FOREIGN KEY ("TutorProfileId") REFERENCES "TutorProfiles"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorReports_TutorBookings_TutorBookingId"
        FOREIGN KEY ("TutorBookingId") REFERENCES "TutorBookings"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TutorReports_Users_ReporterUserId"
        FOREIGN KEY ("ReporterUserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS "IX_TutorReports_TutorProfileId_CreatedAt" ON "TutorReports"("TutorProfileId", "CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_TutorReports_Status"                   ON "TutorReports"("Status");
CREATE INDEX IF NOT EXISTS "IX_TutorReports_ReporterUserId"           ON "TutorReports"("ReporterUserId");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_TutorReports_Booking_Reporter"  ON "TutorReports"("TutorBookingId", "ReporterUserId");
ALTER TABLE "TutorReports" DROP CONSTRAINT IF EXISTS "CK_TutorReports_Status";
ALTER TABLE "TutorReports" ADD CONSTRAINT "CK_TutorReports_Status" CHECK ("Status" IN ('open', 'reviewed'));

-- ── Avis : participants et payeur, un avis par séance et par personne ─────
ALTER TABLE "TutorReviews" ADD COLUMN IF NOT EXISTS "ReviewerRole" VARCHAR(20) NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "UX_TutorReviews_Booking_Author" ON "TutorReviews"("TutorBookingId", "StudentUserId");
-- L'ancien index unique (un seul avis par séance) est retiré APRÈS la
-- création du nouveau ; un index simple le remplace pour les lectures.
DROP INDEX IF EXISTS "IX_TutorReviews_TutorBookingId";
CREATE INDEX IF NOT EXISTS "IX_TutorReviews_TutorBookingId" ON "TutorReviews"("TutorBookingId");

-- ── Contrôle : séances présentielles à venir avec un tuteur non vérifié ───
-- Elles ne sont pas annulées (décision 9.5) ; leur nombre est signalé ici.
DO $$
DECLARE
    n INTEGER;
BEGIN
    SELECT COUNT(*) INTO n
      FROM "TutorBookings" b
      JOIN "TutorProfiles" p ON p."Id" = b."TutorProfileId"
     WHERE b."Mode" IN ('student_home', 'tutor_home', 'neutral_place')
       AND b."Status" IN ('pending_payment', 'pending_tutor_approval', 'confirmed')
       AND b."SessionDate" >= CURRENT_DATE
       AND (p."InPersonVerifiedAt" IS NULL OR p."InPersonSuspendedAt" IS NOT NULL);
    RAISE NOTICE 'Lot 9.5 : % séance(s) présentielle(s) à venir avec un tuteur non vérifié (non annulées).', n;
END $$;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    VARCHAR(150) NOT NULL PRIMARY KEY,
    "ProductVersion" VARCHAR(32)  NOT NULL
);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261012180000_TutorInPersonVerificationAndReports', '8.0.0')
ON CONFLICT DO NOTHING;
