-- Migration: AddCourseGamificationAndCertificates
-- Module 5 — Gamification et certificats vérifiables pour les formations
-- (professeur_complete.md, 5C). Idempotent : peut être rejoué sans casser
-- une base déjà migrée.

CREATE TABLE IF NOT EXISTS "CourseGamificationSettings" (
    "Id"                        SERIAL PRIMARY KEY,
    "CourseId"                  INTEGER NOT NULL,
    "GamificationEnabled"       BOOLEAN NOT NULL DEFAULT FALSE,
    "PointsPerLesson"           INTEGER NOT NULL DEFAULT 10,
    "PointsPerQuiz"             INTEGER NOT NULL DEFAULT 20,
    "PointsBonusPerfectScore"   INTEGER NOT NULL DEFAULT 15,
    "LeaderboardVisible"        BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT "FK_CourseGamificationSettings_Courses_CourseId"
        FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CourseGamificationSettings_CourseId" ON "CourseGamificationSettings"("CourseId");

CREATE TABLE IF NOT EXISTS "StudentCoursePoints" (
    "Id"          SERIAL PRIMARY KEY,
    "CourseId"    INTEGER NOT NULL,
    "UserId"      INTEGER NOT NULL,
    "Points"      INTEGER NOT NULL DEFAULT 0,
    "UpdatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_StudentCoursePoints_CourseId_UserId" ON "StudentCoursePoints"("CourseId", "UserId");

CREATE TABLE IF NOT EXISTS "StudentCourseBadges" (
    "Id"          SERIAL PRIMARY KEY,
    "CourseId"    INTEGER NOT NULL,
    "UserId"      INTEGER NOT NULL,
    "BadgeType"   VARCHAR(30) NOT NULL,
    "EarnedAt"    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_StudentCourseBadges_CourseId_UserId_BadgeType" ON "StudentCourseBadges"("CourseId", "UserId", "BadgeType");

CREATE TABLE IF NOT EXISTS "CourseCertificates" (
    "Id"                 SERIAL PRIMARY KEY,
    "CourseId"           INTEGER NOT NULL,
    "UserId"             INTEGER NOT NULL,
    "VerificationCode"   VARCHAR(40) NOT NULL,
    "Grade"              NUMERIC(5,2),
    "FileUrl"            VARCHAR(500),
    "IssuedAt"           TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_CourseCertificates_Courses_CourseId"
        FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CourseCertificates_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CourseCertificates_VerificationCode" ON "CourseCertificates"("VerificationCode");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CourseCertificates_CourseId_UserId" ON "CourseCertificates"("CourseId", "UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260907210000_AddCourseGamificationAndCertificates', '7.0.5')
ON CONFLICT DO NOTHING;
