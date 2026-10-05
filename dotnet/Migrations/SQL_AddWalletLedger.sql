-- Migration: AddWalletLedger
-- Module 1 (lot 2) : journal de portefeuille unique, décision §14 du suivi.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
--
-- Chemin d'application : ce script, exécuté à la main (psql), comme les
-- autres SQL_*.sql du projet. La classe jumelle 20261003100000_AddWalletLedger.cs
-- ne porte pas d'attribut [Migration] et n'est pas embarquée par
-- `dotnet ef database update`.
--
-- Ordre de déploiement : ce script AVANT le code qui lit WalletTransactions.
-- Puis, une seule fois, la reprise d'historique :
--   POST /api/admin/wallet/backfill   (administrateur)
-- qui rejoue les événements passés en écritures et compare, professeur par
-- professeur, le solde du journal au solde de l'ancien calcul.

CREATE TABLE IF NOT EXISTS "WalletTransactions" (
    "Id"              BIGSERIAL PRIMARY KEY,
    "OwnerType"       VARCHAR(20)  NOT NULL,
    "OwnerId"         INTEGER,
    "EntryType"       VARCHAR(40)  NOT NULL,
    "Amount"          NUMERIC(14,0) NOT NULL,
    "Status"          VARCHAR(20)  NOT NULL DEFAULT 'confirmed',
    "SourceType"      VARCHAR(40),
    "SourceId"        INTEGER,
    "IdempotencyKey"  VARCHAR(120) NOT NULL,
    "ReversesEntryId" BIGINT,
    "Description"     VARCHAR(300) NOT NULL DEFAULT '',
    "CreatedByUserId" INTEGER,
    "OccurredAt"      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "SettledAt"       TIMESTAMP WITH TIME ZONE,
    "ExpiresAt"       TIMESTAMP WITH TIME ZONE,
    CONSTRAINT "CK_WalletTransactions_Status"
        CHECK ("Status" IN ('pending', 'confirmed', 'reversed')),
    CONSTRAINT "CK_WalletTransactions_OwnerType"
        CHECK ("OwnerType" IN ('teacher', 'parent', 'user', 'platform')),
    CONSTRAINT "CK_WalletTransactions_Owner"
        CHECK (("OwnerType" = 'platform') = ("OwnerId" IS NULL)),
    CONSTRAINT "FK_WalletTransactions_Reverses"
        FOREIGN KEY ("ReversesEntryId") REFERENCES "WalletTransactions"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTransactions_IdempotencyKey"
    ON "WalletTransactions" ("IdempotencyKey");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTransactions_ReversesEntryId"
    ON "WalletTransactions" ("ReversesEntryId");
CREATE INDEX IF NOT EXISTS "IX_WalletTransactions_OwnerType_OwnerId_Status"
    ON "WalletTransactions" ("OwnerType", "OwnerId", "Status");
CREATE INDEX IF NOT EXISTS "IX_WalletTransactions_OwnerId_OccurredAt"
    ON "WalletTransactions" ("OwnerId", "OccurredAt");
CREATE INDEX IF NOT EXISTS "IX_WalletTransactions_SourceType_SourceId"
    ON "WalletTransactions" ("SourceType", "SourceId");

-- Registre en ajout seul : la base refuse toute suppression et toute
-- modification du montant, du propriétaire ou de la clé d'une écriture.
-- Seuls le statut et sa date de règlement peuvent évoluer.
CREATE OR REPLACE FUNCTION wallet_transactions_guard() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'WalletTransactions est un registre en ajout seul : suppression interdite (écriture %)', OLD."Id";
    END IF;
    IF NEW."Amount" <> OLD."Amount"
       OR NEW."OwnerType" <> OLD."OwnerType"
       OR NEW."OwnerId" IS DISTINCT FROM OLD."OwnerId"
       OR NEW."EntryType" <> OLD."EntryType"
       OR NEW."IdempotencyKey" <> OLD."IdempotencyKey" THEN
        RAISE EXCEPTION 'WalletTransactions : seule la transition de statut est admise (écriture %)', OLD."Id";
    END IF;
    IF OLD."Status" <> 'pending' AND NEW."Status" <> OLD."Status" THEN
        RAISE EXCEPTION 'WalletTransactions : une écriture % ne change plus de statut (écriture %)', OLD."Status", OLD."Id";
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS "TR_WalletTransactions_Guard" ON "WalletTransactions";
CREATE TRIGGER "TR_WalletTransactions_Guard"
    BEFORE UPDATE OR DELETE ON "WalletTransactions"
    FOR EACH ROW EXECUTE FUNCTION wallet_transactions_guard();

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261003100000_AddWalletLedger', '8.0.0')
ON CONFLICT DO NOTHING;

-- Vérification après application (doit renvoyer 1 ligne chacune) :
-- SELECT to_regclass('"WalletTransactions"');
-- SELECT tgname FROM pg_trigger WHERE tgname = 'TR_WalletTransactions_Guard';
