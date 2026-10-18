-- Migration: AddNotificationUnreadIndexes
-- Index du compteur de notifications non lues (GET /api/notifications/unread-count,
-- badges web et mobile) et de la liste paginée (non lues d'abord, puis les plus
-- récentes). Avant, seul "IX_Notifications_UserId" existait : chaque comptage
-- relisait toutes les lignes de l'utilisateur.
-- Equivalent EF entity: Models/Entities/Notification.cs (aucune colonne ajoutée).
-- Idempotent et indépendant de l'ordre : ne dépend que de la table "Notifications"
-- (créée par InitialCreate) et peut être rejoué sans effet.

CREATE INDEX IF NOT EXISTS "IX_Notifications_UserId_Unread"
    ON "Notifications" ("UserId")
    WHERE "IsRead" = false;

CREATE INDEX IF NOT EXISTS "IX_Notifications_UserId_IsRead_CreatedAt"
    ON "Notifications" ("UserId", "IsRead", "CreatedAt" DESC, "Id" DESC);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261018120000_AddNotificationUnreadIndexes', '8.0.0')
ON CONFLICT DO NOTHING;
