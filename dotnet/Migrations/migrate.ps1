# Win+ User Settings Implementation - EF Core Migration Commands
# Execute these commands in PowerShell from the backend directory

Write-Host "=== Win+ Database Migration Setup ===" -ForegroundColor Cyan

# Step 1: Navigate to backend directory
Write-Host "`n[STEP 1] Navigate to backend dotnet directory..."
Set-Location "m:\win\winplus\backend\dotnet"

# Step 2: Run SQL validation script
Write-Host "`n[STEP 2] Run SQL Validation Script..." -ForegroundColor Yellow
Write-Host "Before applying migrations, verify the database state:"
Write-Host "Connect to PostgreSQL and run: Migrations/SQL_Validation_Check.sql"
Write-Host "Example: psql -U postgres -d winplus < Migrations/SQL_Validation_Check.sql"
Read-Host "Press Enter after running validation script"

# Step 3: Generate EF Core migration
Write-Host "`n[STEP 3] Generate EF Core Migration..." -ForegroundColor Yellow
Write-Host "Running: dotnet ef migrations add AddUserSettingsTables"
dotnet ef migrations add AddUserSettingsTables --context ApplicationDbContext

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error generating migration!" -ForegroundColor Red
    exit 1
}

Write-Host "Migration generated successfully" -ForegroundColor Green

# Step 4: Review migration file
Write-Host "`n[STEP 4] Review Generated Migration..." -ForegroundColor Yellow
Write-Host "Check the generated file in Migrations/ folder (name like: [timestamp]_AddUserSettingsTables.cs)"
Write-Host "Review Up() and Down() methods to ensure correctness"
Read-Host "Press Enter after reviewing migration"

# Step 5: Apply migration to database
Write-Host "`n[STEP 5] Apply Migration to Database..." -ForegroundColor Yellow
Write-Host "Running: dotnet ef database update"
dotnet ef database update --context ApplicationDbContext

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error applying migration!" -ForegroundColor Red
    exit 1
}

Write-Host "Migration applied successfully" -ForegroundColor Green

# Step 6: Verify migration
Write-Host "`n[STEP 6] Verify Migration..." -ForegroundColor Yellow
Write-Host "Connect to PostgreSQL and verify tables were created:"
Write-Host "SELECT MigrationId FROM `"__EFMigrationsHistory`" ORDER BY MigrationId DESC LIMIT 5;"
Write-Host ""
Write-Host "Or use: Migrations/SQL_Validation_Check.sql to verify table creation"

# Step 7: Verify DI registration
Write-Host "`n[STEP 7] Verify Services Registered..." -ForegroundColor Yellow
Write-Host "Check Program.cs for these lines:"
Write-Host "  builder.Services.AddScoped<ISettingsService, SettingsService>();"
Write-Host "  builder.Services.AddScoped<ISessionService, SessionService>();"
Write-Host "  builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();"

# Step 8: Test compilation
Write-Host "`n[STEP 8] Test Compilation..." -ForegroundColor Yellow
Write-Host "Running: dotnet build"
dotnet build

if ($LASTEXITCODE -ne 0) {
    Write-Host "Compilation errors found!" -ForegroundColor Red
    exit 1
}

Write-Host "Compilation successful" -ForegroundColor Green

Write-Host "`n=== Migration Complete ===" -ForegroundColor Cyan
Write-Host "All services are now ready for use:" -ForegroundColor Green
Write-Host "  - ISettingsService (Notification & Privacy settings)"
Write-Host "  - ISessionService (User session management)"
Write-Host "  - ITwoFactorService (2FA implementation)"
