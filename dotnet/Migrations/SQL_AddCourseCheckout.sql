-- Migration: AddCourseCheckout
-- B1 (lot 4, point ouvert) : achat d'une formation payante (Course, pas
-- Subject). Avant cette migration, rien ne permettait de créer un OrderItem
-- avec CourseId renseigné pour une vraie vente : CartItem n'avait aucun
-- champ CourseId, et OrderItems.SubjectId était NOT NULL (donc toujours
-- requis), même si la colonne OrderItems.CourseId existait déjà (voir
-- SQL_FixOrderItemsMissingColumns.sql) sans jamais être alimentée par un
-- parcours réel. CourseEnrollmentController.Enroll vérifiait déjà
-- OrderItems.CourseId coté accès (`oi.CourseId == id && ...`) : seul le
-- chemin panier → commande manquait.
-- Equivalent EF entities: Models/Entities/CartItem.cs, Models/Entities/OrderItem.cs,
-- Data/ApplicationDbContext.cs (OnModelCreating, CartItem/OrderItem)
-- Idempotent : peut être rejoué sans casser une base déjà migrée.

-- ── OrderItems : SubjectId devient nullable, CHECK xor avec CourseId ───────
ALTER TABLE "OrderItems" ALTER COLUMN "SubjectId" DROP NOT NULL;

ALTER TABLE "OrderItems" DROP CONSTRAINT IF EXISTS "CK_OrderItems_SubjectOrCourse";
ALTER TABLE "OrderItems"
    ADD CONSTRAINT "CK_OrderItems_SubjectOrCourse"
    CHECK (
        ("SubjectId" IS NOT NULL AND "CourseId" IS NULL) OR
        ("SubjectId" IS NULL AND "CourseId" IS NOT NULL)
    );

-- ── CartItems : CourseId ajouté, SubjectId devient nullable ───────────────
ALTER TABLE "CartItems" ADD COLUMN IF NOT EXISTS "CourseId" INTEGER;
ALTER TABLE "CartItems" ALTER COLUMN "SubjectId" DROP NOT NULL;

ALTER TABLE "CartItems" DROP CONSTRAINT IF EXISTS "CK_CartItems_SubjectOrCourse";
ALTER TABLE "CartItems"
    ADD CONSTRAINT "CK_CartItems_SubjectOrCourse"
    CHECK (
        ("SubjectId" IS NOT NULL AND "CourseId" IS NULL) OR
        ("SubjectId" IS NULL AND "CourseId" IS NOT NULL)
    );

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_CartItems_Courses_CourseId'
    ) THEN
        ALTER TABLE "CartItems"
            ADD CONSTRAINT "FK_CartItems_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CartItems_CourseId" ON "CartItems"("CourseId");

-- Les anciens index uniques (UserId, SubjectId) / (DeviceId, SubjectId) ne
-- protégeaient qu'un doublon de Subject au panier. Postgres traite NULL
-- comme distinct dans un index unique filtré : sans le filtre explicite sur
-- "SubjectId" IS NOT NULL, l'index existant continue de fonctionner tel quel
-- pour les lignes Subject, mais on le recrée avec le filtre complet pour
-- qu'il reste cohérent avec la config EF (ApplicationDbContext) et qu'un
-- doublon de CourseId soit lui aussi bloqué par son propre index.
DROP INDEX IF EXISTS "IX_CartItems_UserId_SubjectId";
DROP INDEX IF EXISTS "IX_CartItems_DeviceId_SubjectId";

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CartItems_UserId_SubjectId"
    ON "CartItems" ("UserId", "SubjectId") WHERE "UserId" IS NOT NULL AND "SubjectId" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CartItems_DeviceId_SubjectId"
    ON "CartItems" ("DeviceId", "SubjectId") WHERE "DeviceId" IS NOT NULL AND "SubjectId" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CartItems_UserId_CourseId"
    ON "CartItems" ("UserId", "CourseId") WHERE "UserId" IS NOT NULL AND "CourseId" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CartItems_DeviceId_CourseId"
    ON "CartItems" ("DeviceId", "CourseId") WHERE "DeviceId" IS NOT NULL AND "CourseId" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005170000_AddCourseCheckout', '8.0.0')
ON CONFLICT DO NOTHING;
