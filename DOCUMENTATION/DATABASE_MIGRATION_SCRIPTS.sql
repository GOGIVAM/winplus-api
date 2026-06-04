-- ============================================================================
-- MIGRATION SCRIPTS - Custom Authentication System
-- Database: winplus_db
-- Host: 172.31.20.230
-- User: gogivam
-- ============================================================================

-- Step 1: Create RefreshToken table
-- ============================================================================
CREATE TABLE IF NOT EXISTS "RefreshTokens" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL,
    "Token" VARCHAR(500) NOT NULL UNIQUE,
    "ExpiresAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "RevokedAt" TIMESTAMP WITH TIME ZONE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "RevokedByIp" VARCHAR(45),
    FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX idx_refresh_tokens_user_id ON "RefreshTokens"("UserId");
CREATE INDEX idx_refresh_tokens_token ON "RefreshTokens"("Token");
CREATE INDEX idx_refresh_tokens_expires_at ON "RefreshTokens"("ExpiresAt");

-- ============================================================================
-- Step 2: Create DeviceInfo table (for Remember Me)
-- ============================================================================
CREATE TABLE IF NOT EXISTS "DeviceInfos" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL,
    "DeviceFingerprint" VARCHAR(64) NOT NULL,
    "UserAgent" VARCHAR(500),
    "IpAddress" VARCHAR(45),
    "BrowserName" VARCHAR(50),
    "BrowserVersion" VARCHAR(20),
    "OSName" VARCHAR(50),
    "OSVersion" VARCHAR(20),
    "DeviceType" VARCHAR(50),
    "RememberUntil" TIMESTAMP WITH TIME ZONE,
    "LastUsedAt" TIMESTAMP WITH TIME ZONE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX idx_device_infos_user_id ON "DeviceInfos"("UserId");
CREATE INDEX idx_device_infos_fingerprint ON "DeviceInfos"("DeviceFingerprint");
CREATE INDEX idx_device_infos_remember ON "DeviceInfos"("RememberUntil");
CREATE UNIQUE INDEX idx_device_infos_unique ON "DeviceInfos"("UserId", "DeviceFingerprint");

-- ============================================================================
-- Step 3: Create EmailVerificationToken table
-- ============================================================================
CREATE TABLE IF NOT EXISTS "EmailVerificationTokens" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL,
    "VerificationCode" VARCHAR(6) NOT NULL,
    "ExpiresAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "IsVerified" BOOLEAN NOT NULL DEFAULT FALSE,
    "AttemptCount" INTEGER NOT NULL DEFAULT 0,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "VerifiedAt" TIMESTAMP WITH TIME ZONE,
    FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX idx_email_verification_tokens_user_id ON "EmailVerificationTokens"("UserId");
CREATE INDEX idx_email_verification_tokens_code ON "EmailVerificationTokens"("VerificationCode");
CREATE INDEX idx_email_verification_tokens_expires ON "EmailVerificationTokens"("ExpiresAt");

-- ============================================================================
-- Step 4: Create PasswordResetToken table
-- ============================================================================
CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL,
    "Token" VARCHAR(500) NOT NULL UNIQUE,
    "ExpiresAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "IsUsed" BOOLEAN NOT NULL DEFAULT FALSE,
    "UsedAt" TIMESTAMP WITH TIME ZONE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX idx_password_reset_tokens_user_id ON "PasswordResetTokens"("UserId");
CREATE INDEX idx_password_reset_tokens_token ON "PasswordResetTokens"("Token");
CREATE INDEX idx_password_reset_tokens_expires ON "PasswordResetTokens"("ExpiresAt");

-- ============================================================================
-- Step 5: Create TwoFactorToken table (for TOTP & Backup Codes)
-- ============================================================================
CREATE TABLE IF NOT EXISTS "TwoFactorTokens" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL UNIQUE,
    "TotpSecret" VARCHAR(32),
    "IsTotpEnabled" BOOLEAN NOT NULL DEFAULT FALSE,
    "BackupCodesCount" INTEGER NOT NULL DEFAULT 0,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE,
    FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX idx_two_factor_tokens_user_id ON "TwoFactorTokens"("UserId");

-- ============================================================================
-- Step 6: Create BackupCode table (2FA recovery codes)
-- ============================================================================
CREATE TABLE IF NOT EXISTS "BackupCodes" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TwoFactorTokenId" UUID NOT NULL,
    "Code" VARCHAR(20) NOT NULL UNIQUE,
    "IsUsed" BOOLEAN NOT NULL DEFAULT FALSE,
    "UsedAt" TIMESTAMP WITH TIME ZONE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY ("TwoFactorTokenId") REFERENCES "TwoFactorTokens"("Id") ON DELETE CASCADE
);

CREATE INDEX idx_backup_codes_two_factor_id ON "BackupCodes"("TwoFactorTokenId");
CREATE INDEX idx_backup_codes_code ON "BackupCodes"("Code");

-- ============================================================================
-- Step 7: Create OAuthAccount table (for Google & Facebook login)
-- ============================================================================
CREATE TABLE IF NOT EXISTS "OAuthAccounts" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "UserId" UUID NOT NULL,
    "Provider" VARCHAR(50) NOT NULL,
    "ProviderUserId" VARCHAR(255) NOT NULL,
    "DisplayName" VARCHAR(255),
    "ProfileImageUrl" VARCHAR(500),
    "Email" VARCHAR(255),
    "ConnectedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "DisconnectedAt" TIMESTAMP WITH TIME ZONE,
    FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE CASCADE
);

CREATE INDEX idx_oauth_accounts_user_id ON "OAuthAccounts"("UserId");
CREATE UNIQUE INDEX idx_oauth_accounts_unique ON "OAuthAccounts"("Provider", "ProviderUserId");

-- ============================================================================
-- Step 8: Update Users table with new columns (if not already present)
-- ============================================================================
-- Add columns only if they don't exist
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Users' AND column_name = 'EmailVerified'
    ) THEN
        ALTER TABLE "Users" ADD COLUMN "EmailVerified" BOOLEAN NOT NULL DEFAULT FALSE;
    END IF;
    
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Users' AND column_name = 'PasswordHash'
    ) THEN
        ALTER TABLE "Users" ADD COLUMN "PasswordHash" VARCHAR(255);
    END IF;
    
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'Users' AND column_name = 'LastLoginAt'
    ) THEN
        ALTER TABLE "Users" ADD COLUMN "LastLoginAt" TIMESTAMP WITH TIME ZONE;
    END IF;
END $$;

-- ============================================================================
-- VERIFICATION SCRIPT - Run after migration
-- ============================================================================
-- Check if all tables are created correctly:
/*
SELECT table_name FROM information_schema.tables 
WHERE table_schema = 'public' 
AND table_name IN ('RefreshTokens', 'DeviceInfos', 'EmailVerificationTokens', 
                   'PasswordResetTokens', 'TwoFactorTokens', 'BackupCodes', 'OAuthAccounts');

-- Check Users table columns:
SELECT column_name, data_type FROM information_schema.columns 
WHERE table_name = 'Users' 
ORDER BY ordinal_position;
*/

-- ============================================================================
-- ROLLBACK SCRIPT (if needed)
-- ============================================================================
/*
DROP TABLE IF EXISTS "OAuthAccounts" CASCADE;
DROP TABLE IF EXISTS "BackupCodes" CASCADE;
DROP TABLE IF EXISTS "TwoFactorTokens" CASCADE;
DROP TABLE IF EXISTS "PasswordResetTokens" CASCADE;
DROP TABLE IF EXISTS "EmailVerificationTokens" CASCADE;
DROP TABLE IF EXISTS "DeviceInfos" CASCADE;
DROP TABLE IF EXISTS "RefreshTokens" CASCADE;

-- Restore Users table if columns need to be removed:
ALTER TABLE "Users" DROP COLUMN IF EXISTS "EmailVerified";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "PasswordHash";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "LastLoginAt";
*/
