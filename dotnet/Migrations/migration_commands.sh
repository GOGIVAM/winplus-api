#!/bin/bash
# EF Core Migration Commands for Win+ User Settings Implementation

# Navigate to backend directory
# cd m:\win\winplus\backend\dotnet

# STEP 1: Run SQL validation to check current database state
# Execute this query against your PostgreSQL database:
# cat Migrations/SQL_Validation_Check.sql

# STEP 2: Generate EF Core migration
# Run this command to create migration files:
dotnet ef migrations add AddUserSettingsTables --project . --context ApplicationDbContext

# STEP 3: Review the generated migration (optional but recommended)
# Check the generated migration file in Migrations/ folder:
# Review migration Up() and Down() methods to ensure correctness

# STEP 4: Apply migration to database
# Run this command to execute migration against database:
dotnet ef database update --context ApplicationDbContext

# STEP 5: Verify migration was applied
# Connect to database and run this query:
# SELECT MigrationId FROM "__EFMigrationsHistory" ORDER BY MigrationId DESC;

# STEP 6: Register Services in Dependency Injection (Startup.cs or Program.cs)
# Add these lines to your service registration:

# In Program.cs:
# builder.Services.AddScoped<ISettingsService, SettingsService>();
# builder.Services.AddScoped<ISessionService, SessionService>();
# builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();

echo "Migration script ready. Run commands above in order."
