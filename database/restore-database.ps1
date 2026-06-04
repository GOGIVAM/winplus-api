# Script PowerShell de restauration PostgreSQL
# Utilisation : .\restore-database.ps1 -BackupFile '<chemin_backup>'
# Exemple : .\restore-database.ps1 -BackupFile 'backups\backup_20240105_120000.sql'

param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFile,
    
    [string]$Host = "98.86.67.128",
    [string]$Port = "5432",
    [string]$User = "gogivam",
    [string]$Password = "Admin001",
    [string]$Database = "winplus_db"
)

# Vérifier que le fichier existe
if (-not (Test-Path $BackupFile)) {
    Write-Host "❌ Fichier non trouvé: $BackupFile" -ForegroundColor Red
    Write-Host ""
    Write-Host "Fichiers disponibles :" -ForegroundColor Yellow
    Get-ChildItem ".\backups\" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  $($_.Name)" -ForegroundColor Gray
    }
    exit 1
}

# Si le fichier est en .zip, le décompresser
$tempDir = $null
if ($BackupFile -like "*.zip") {
    Write-Host "✓ Décompression du fichier..." -ForegroundColor Green
    $tempDir = New-Item -ItemType Directory -Path ([System.IO.Path]::GetTempPath() + [System.Guid]::NewGuid()) -Force
    Expand-Archive -Path $BackupFile -DestinationPath $tempDir -Force
    $BackupFile = Get-ChildItem $tempDir -Filter "*.sql" | Select-Object -First 1 -ExpandProperty FullName
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Restauration de la base de données" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Host: $Host" -ForegroundColor Gray
Write-Host "Database: $Database" -ForegroundColor Gray
Write-Host "Fichier: $BackupFile" -ForegroundColor Gray
Write-Host ""

# Vérifier que psql est disponible
$psqlPath = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psqlPath) {
    Write-Host "❌ psql n'est pas disponible. Installez PostgreSQL Client." -ForegroundColor Red
    exit 1
}

# ATTENTION : Demander confirmation
Write-Host "⚠️  ATTENTION: Cela supprimera toutes les données actuelles!" -ForegroundColor Red
Write-Host "Êtes-vous sûr de vouloir continuer? (tapez 'OUI' pour confirmer)" -ForegroundColor Yellow
$confirmation = Read-Host

if ($confirmation -ne "OUI") {
    Write-Host "❌ Restauration annulée." -ForegroundColor Red
    if ($tempDir) { Remove-Item $tempDir -Recurse -Force }
    exit 0
}

$env:PGPASSWORD = $Password

try {
    # Supprimer la base de données existante
    Write-Host "✓ Suppression de la base de données existante..." -ForegroundColor Green
    & psql -h $Host -U $User -p $Port -tc "DROP DATABASE IF EXISTS $Database;" | Out-Null

    # Créer une nouvelle base de données
    Write-Host "✓ Création d'une nouvelle base de données..." -ForegroundColor Green
    & psql -h $Host -U $User -p $Port -c "CREATE DATABASE $Database;" | Out-Null

    # Restaurer les données
    Write-Host "✓ Restauration des données..." -ForegroundColor Green
    $backupContent = Get-Content $BackupFile -Raw
    & psql -h $Host -U $User -p $Port -d $Database -c $backupContent | Out-Null

    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "✅ Restauration complète!" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Vérification de la restauration :" -ForegroundColor Yellow
    Write-Host "  psql -h $Host -U $User -d $Database -c \"\dt\"" -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-Host "❌ Erreur: $_" -ForegroundColor Red
    exit 1
}
finally {
    $env:PGPASSWORD = ""
    if ($tempDir) { Remove-Item $tempDir -Recurse -Force }
}
