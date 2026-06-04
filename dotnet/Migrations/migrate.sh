#!/bin/bash
# Win+ User Settings Implementation - EF Core Migration Commands
# Execute these commands in bash from the backend directory

echo "=== Win+ Database Migration Setup ==="

# Step 1: Navigate to backend directory
echo ""
echo "[STEP 1] Navigate to backend dotnet directory..."
cd m:/win/winplus/backend/dotnet || exit 1

# Step 2: Run SQL validation script
echo ""
echo "[STEP 2] Run SQL Validation Script..."
echo "Before applying migrations, verify the database state:"
echo "Connect to PostgreSQL and run: Migrations/SQL_Validation_Check.sql"
echo "Example: psql -U postgres -d winplus < Migrations/SQL_Validation_Check.sql"
read -p "Press Enter after running validation script: "

# Step 3: Generate EF Core migration
echo ""
echo "[STEP 3] Generate EF Core Migration..."
echo "Running: dotnet ef migrations add AddUserSettingsTables"
dotnet ef migrations add AddUserSettingsTables --context ApplicationDbContext

if [ $? -ne 0 ]; then
    echo "Error generating migration!"
    exit 1
fi

echo "Migration generated successfully"

# Step 4: Review migration file
echo ""
echo "[STEP 4] Review Generated Migration..."
echo "Check the generated file in Migrations/ folder (name like: [timestamp]_AddUserSettingsTables.cs)"
echo "Review Up() and Down() methods to ensure correctness"
read -p "Press Enter after reviewing migration: "

# Step 5: Apply migration to database
echo ""
echo "[STEP 5] Apply Migration to Database..."
echo "Running: dotnet ef database update"
dotnet ef database update --context ApplicationDbContext

if [ $? -ne 0 ]; then
    echo "Error applying migration!"
    exit 1
fi

echo "Migration applied successfully"

# Step 6: Verify migration
echo ""
echo "[STEP 6] Verify Migration..."
echo "Connect to PostgreSQL and verify tables were created:"
echo "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId DESC LIMIT 5;"
echo ""
echo "Or use: Migrations/SQL_Validation_Check.sql to verify table creation"

# Step 7: Test compilation
echo ""
echo "[STEP 7] Test Compilation..."
echo "Running: dotnet build"
dotnet build

if [ $? -ne 0 ]; then
    echo "Compilation errors found!"
    exit 1
fi

echo "Compilation successful"

echo ""
echo "=== Migration Complete ==="
echo "All services are now ready for use:"
echo "  - ISettingsService (Notification & Privacy settings)"
echo "  - ISessionService (User session management)"
echo "  - ITwoFactorService (2FA implementation)"
