-- Migration: FixOrderItemsMissingColumns
-- OrderItem.CourseId / OrderItem.Course (Models/Entities/OrderItem.cs) ont été
-- ajoutés côté C# pour la vente de formations, mais aucune migration EF ni
-- script SQL n'a jamais ajouté la colonne correspondante : le modèle EF
-- suivi (ApplicationDbContextModelSnapshot.cs) ne connaît même pas
-- "CourseId" sur OrderItem. Résultat : chaque requête sur GET /api/orders
-- (OrderRepository.GetByUserIdAsync → .Include(o => o.Items)) tente de
-- sélectionner une colonne absente en base et échoue en 500.
-- Idempotent : peut être rejoué sans casser une base déjà à jour.

ALTER TABLE "OrderItems" ADD COLUMN IF NOT EXISTS "CourseId" INTEGER;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_OrderItems_Courses_CourseId'
    ) THEN
        ALTER TABLE "OrderItems"
            ADD CONSTRAINT "FK_OrderItems_Courses_CourseId"
            FOREIGN KEY ("CourseId") REFERENCES "Courses"("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_OrderItems_CourseId" ON "OrderItems"("CourseId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260911120100_FixOrderItemsMissingColumns', '8.0.0')
ON CONFLICT DO NOTHING;
