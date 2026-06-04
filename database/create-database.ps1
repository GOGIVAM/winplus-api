# Script PowerShell de création de base de données PostgreSQL
# Utilisation : .\create-database.ps1

param(
    [string]$Host = "98.86.67.128",
    [string]$Port = "5432",
    [string]$User = "gogivam",
    [string]$Password = "Admin001",
    [string]$Database = "winplus_db"
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Création de la base de données PostgreSQL" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Host: $Host" -ForegroundColor Gray
Write-Host "User: $User" -ForegroundColor Gray
Write-Host "Database: $Database" -ForegroundColor Gray
Write-Host ""

# Vérifier que psql est disponible
$psqlPath = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psqlPath) {
    Write-Host "❌ psql n'est pas disponible. Installez PostgreSQL Client." -ForegroundColor Red
    Write-Host "   Télécharger depuis : https://www.postgresql.org/download/windows/" -ForegroundColor Yellow
    exit 1
}

# Créer la base de données
Write-Host "✓ Vérification/création de la base de données..." -ForegroundColor Green

$env:PGPASSWORD = $Password

try {
    # Vérifier si la base de données existe déjà
    $checkDb = & psql -h $Host -U $User -p $Port -tc "SELECT 1 FROM pg_database WHERE datname = '$Database'"
    
    if (-not $checkDb) {
        Write-Host "  → Base de données n'existe pas, création en cours..." -ForegroundColor Yellow
        & psql -h $Host -U $User -p $Port -c "CREATE DATABASE $Database;" | Out-Null
        Write-Host "  ✓ Base de données créée" -ForegroundColor Green
    }
    else {
        Write-Host "  → Base de données existe déjà" -ForegroundColor Yellow
    }

    # Exécuter le schéma SQL
    Write-Host "✓ Exécution du schéma SQL..." -ForegroundColor Green
    
    if (-not (Test-Path "schema.sql")) {
        Write-Host "❌ Fichier schema.sql non trouvé!" -ForegroundColor Red
        exit 1
    }

    $schemaContent = Get-Content "schema.sql" -Raw
    & psql -h $Host -U $User -p $Port -d $Database -c $schemaContent | Out-Null
    
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "✅ Base de données créée avec succès!" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Connexion à la base de données :" -ForegroundColor Yellow
    Write-Host "  psql -h $Host -U $User -p $Port -d $Database" -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-Host "❌ Erreur: $_" -ForegroundColor Red
    exit 1
}
finally {
    $env:PGPASSWORD = ""
}
