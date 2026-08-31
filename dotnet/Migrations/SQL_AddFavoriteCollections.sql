-- Migration: AddFavoriteCollections
-- Equivalent of EF migration 20260120_AddFavoriteCollections

CREATE TABLE IF NOT EXISTS "FavoriteCollections" (
    "Id"          SERIAL PRIMARY KEY,
    "UserId"      INTEGER NOT NULL,
    "Name"        VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "Color"       VARCHAR(20),
    "Icon"        VARCHAR(50),
    "Order"       INTEGER NOT NULL DEFAULT 0,
    "CreatedAt"   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt"   TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "FK_FavoriteCollections_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_FavoriteCollections_Name_UserId"
    ON "FavoriteCollections"("Name", "UserId");

CREATE INDEX IF NOT EXISTS "IX_FavoriteCollections_UserId"
    ON "FavoriteCollections"("UserId");

-- Add CollectionId to Favorites (nullable FK)
ALTER TABLE "Favorites"
    ADD COLUMN IF NOT EXISTS "CollectionId" INTEGER;

CREATE INDEX IF NOT EXISTS "IX_Favorites_CollectionId"
    ON "Favorites"("CollectionId");

ALTER TABLE "Favorites"
    DROP CONSTRAINT IF EXISTS "FK_Favorites_FavoriteCollections_CollectionId";

ALTER TABLE "Favorites"
    ADD CONSTRAINT "FK_Favorites_FavoriteCollections_CollectionId"
        FOREIGN KEY ("CollectionId") REFERENCES "FavoriteCollections"("Id") ON DELETE SET NULL;
