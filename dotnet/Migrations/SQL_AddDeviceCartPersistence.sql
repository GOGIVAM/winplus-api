-- Migration: AddDeviceCartPersistence
-- Remplace le panier anonyme en mémoire (AnonymousCartService, supprimé  un
-- dictionnaire statique qui ne survivait pas à un redémarrage du service,
-- cause du bug "Cart is empty" après connexion) par une persistance directe
-- dans CartItems : UserId devient nullable, un DeviceId le remplace pour les
-- items anonymes. Voir CartItem.cs pour le raisonnement complet.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

-- UserId nullable : un item anonyme n'a pas encore d'utilisateur.
ALTER TABLE "CartItems" ALTER COLUMN "UserId" DROP NOT NULL;

ALTER TABLE "CartItems"
    ADD COLUMN IF NOT EXISTS "DeviceId" VARCHAR(200);

-- L'ancien index unique (UserId, SubjectId) ne distinguait pas les items
-- anonymes (alors inexistants dans cette table) ; remplacé par deux index
-- partiels, un par mode (UserId renseigné XOR DeviceId renseigné).
DROP INDEX IF EXISTS "IX_CartItems_UserId_SubjectId";

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CartItems_UserId_SubjectId"
    ON "CartItems"("UserId", "SubjectId") WHERE "UserId" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CartItems_DeviceId_SubjectId"
    ON "CartItems"("DeviceId", "SubjectId") WHERE "DeviceId" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260916120000_AddDeviceCartPersistence', '8.0.0')
ON CONFLICT DO NOTHING;
