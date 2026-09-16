-- Migration: AddParentAlertAndParentReport
-- Historique consultable des alertes WinAI et des rapports destinés aux parents.
-- Jusqu'ici : les alertes étaient recalculées à la volée (jamais stockées) et les rapports
-- soit envoyés par email sans trace, soit renvoyés en texte brut sans persistance — ce dernier
-- finissait par erreur dans DirectMessage faute de canal dédié (corrigé côté API séparément).
-- Ces deux tables sont volontairement distinctes de "DirectMessages" et de "Notifications" :
-- rôles différents, ne jamais y faire migrer ces données.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

CREATE TABLE IF NOT EXISTS "ParentAlerts" (
    "Id"         SERIAL PRIMARY KEY,
    "ParentId"   INTEGER NOT NULL,
    "ChildId"    INTEGER NOT NULL,
    "Type"       VARCHAR(30) NOT NULL,
    "Severity"   VARCHAR(10) NOT NULL DEFAULT 'Low',
    "Content"    TEXT NOT NULL,
    "IsRead"     BOOLEAN NOT NULL DEFAULT FALSE,
    "DetectedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "CreatedAt"  TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_ParentAlerts_Users_ParentId"
        FOREIGN KEY ("ParentId") REFERENCES "Users"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ParentAlerts_Users_ChildId"
        FOREIGN KEY ("ChildId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS "IX_ParentAlerts_ParentId" ON "ParentAlerts"("ParentId");
CREATE INDEX IF NOT EXISTS "IX_ParentAlerts_ChildId" ON "ParentAlerts"("ChildId");
CREATE INDEX IF NOT EXISTS "IX_ParentAlerts_ParentId_ChildId_IsRead" ON "ParentAlerts"("ParentId", "ChildId", "IsRead");

CREATE TABLE IF NOT EXISTS "ParentReports" (
    "Id"          SERIAL PRIMARY KEY,
    "ParentId"    INTEGER NOT NULL,
    "ChildId"     INTEGER,
    "ReportType"  VARCHAR(20) NOT NULL,
    "Content"     TEXT,
    "CapsuleText" VARCHAR(500),
    "EmitterType" VARCHAR(10) NOT NULL DEFAULT 'System',
    "EmitterId"   INTEGER,
    "IsRead"      BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT "FK_ParentReports_Users_ParentId"
        FOREIGN KEY ("ParentId") REFERENCES "Users"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ParentReports_Users_ChildId"
        FOREIGN KEY ("ChildId") REFERENCES "Users"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ParentReports_Users_EmitterId"
        FOREIGN KEY ("EmitterId") REFERENCES "Users"("Id") ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS "IX_ParentReports_ParentId" ON "ParentReports"("ParentId");
CREATE INDEX IF NOT EXISTS "IX_ParentReports_ChildId" ON "ParentReports"("ChildId");
CREATE INDEX IF NOT EXISTS "IX_ParentReports_ParentId_ChildId_ReportType" ON "ParentReports"("ParentId", "ChildId", "ReportType");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260915221500_AddParentAlertAndParentReport', '8.0.0')
ON CONFLICT DO NOTHING;
