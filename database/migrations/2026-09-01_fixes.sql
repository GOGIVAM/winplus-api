-- WinPlus  Migration fixes 2026-09-01
-- Run once on production DB
-- psql -U winplus -d winplus -f 2026-09-01_fixes.sql

BEGIN;

-- ─── 1. Certificates: add missing column ─────────────────────────────────────
-- The C# entity uses [Column("CertificateUrl")] and [Column("FinalScore")] to map
-- to existing DB columns. Only VerificationCode is missing.
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'Certificates' AND column_name = 'VerificationCode'
  ) THEN
    ALTER TABLE public."Certificates" ADD COLUMN "VerificationCode" VARCHAR(100);
  END IF;
END$$;

-- ─── 2. PromoCodes table ─────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public."PromoCodes" (
  "Id"                   SERIAL PRIMARY KEY,
  "Code"                 VARCHAR(50)     NOT NULL UNIQUE,
  "Description"          VARCHAR(500),
  "DiscountType"         VARCHAR(20)     NOT NULL DEFAULT 'Percentage',
  "DiscountValue"        NUMERIC(18,2)   NOT NULL,
  "MinimumPurchase"      NUMERIC(18,2),
  "MaximumDiscount"      NUMERIC(18,2),
  "UsageLimit"           INTEGER,
  "UsageCount"           INTEGER         NOT NULL DEFAULT 0,
  "PerUserLimit"         INTEGER         DEFAULT 1,
  "ValidFrom"            TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
  "ValidUntil"           TIMESTAMPTZ,
  "IsActive"             BOOLEAN         NOT NULL DEFAULT TRUE,
  "ApplicableSubjectIds" TEXT,
  "CreatedBy"            INTEGER         NOT NULL REFERENCES public."Users"("Id") ON DELETE CASCADE,
  "CreatedAt"            TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
  "UpdatedAt"            TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_promocodes_code     ON public."PromoCodes" ("Code");
CREATE INDEX IF NOT EXISTS idx_promocodes_active   ON public."PromoCodes" ("IsActive");
CREATE INDEX IF NOT EXISTS idx_promocodes_validuntil ON public."PromoCodes" ("ValidUntil");

-- ─── 3. PromoCodeUsages table ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS public."PromoCodeUsages" (
  "Id"             SERIAL PRIMARY KEY,
  "PromoCodeId"    INTEGER       NOT NULL REFERENCES public."PromoCodes"("Id") ON DELETE CASCADE,
  "UserId"         INTEGER       NOT NULL REFERENCES public."Users"("Id")      ON DELETE CASCADE,
  "OrderId"        INTEGER       NOT NULL REFERENCES public."Orders"("Id")     ON DELETE CASCADE,
  "DiscountAmount" NUMERIC(18,2) NOT NULL,
  "UsedAt"         TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_promoCodeUsages_promoCode ON public."PromoCodeUsages" ("PromoCodeId");
CREATE INDEX IF NOT EXISTS idx_promoCodeUsages_user      ON public."PromoCodeUsages" ("UserId");

COMMIT;
