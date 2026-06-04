-- SQL Validation Script for Win+ User Settings Tables
-- Run this BEFORE applying migrations to check current state

-- Check if UserNotificationSettings table exists
SELECT EXISTS (
    SELECT 1 FROM information_schema.TABLES 
    WHERE TABLE_SCHEMA = 'public' 
    AND TABLE_NAME = 'UserNotificationSettings'
) as UserNotificationSettings_Exists;

-- Check if UserPrivacySettings table exists
SELECT EXISTS (
    SELECT 1 FROM information_schema.TABLES 
    WHERE TABLE_SCHEMA = 'public' 
    AND TABLE_NAME = 'UserPrivacySettings'
) as UserPrivacySettings_Exists;

-- Check if UserTwoFactorAuthentication table exists
SELECT EXISTS (
    SELECT 1 FROM information_schema.TABLES 
    WHERE TABLE_SCHEMA = 'public' 
    AND TABLE_NAME = 'UserTwoFactorAuthentication'
) as UserTwoFactorAuthentication_Exists;

-- Check if UserSessions table exists
SELECT EXISTS (
    SELECT 1 FROM information_schema.TABLES 
    WHERE TABLE_SCHEMA = 'public' 
    AND TABLE_NAME = 'UserSessions'
) as UserSessions_Exists;

-- Check total existing tables
SELECT COUNT(*) as TotalTables FROM information_schema.TABLES WHERE TABLE_SCHEMA = 'public';

-- Get current schema version from EF Core migrations history (if exists)
SELECT EXISTS (
    SELECT 1 FROM information_schema.TABLES 
    WHERE TABLE_SCHEMA = 'public' 
    AND TABLE_NAME = '__EFMigrationsHistory'
) as EFMigrationsHistory_Exists;

-- If __EFMigrationsHistory exists, show last migration
SELECT MigrationId FROM "__EFMigrationsHistory" ORDER BY MigrationId DESC LIMIT 5;
