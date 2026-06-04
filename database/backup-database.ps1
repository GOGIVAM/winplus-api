# Script PowerShell de sauvegarde PostgreSQL
# Utilisation : .\backup-database.ps1
# Crée un fichier backup_YYYYMMDD_HHMMSS.sql

param(
    [string]$Host = "98.86.67.128",
    [string]$Port = "5432",
    [string]$User = "gogivam",
    [string]$Password = "Admin001",
    [string]$Database = "winplus_db",
    [string]$BackupDir = ".\backups"
)

# Créer le répertoire de sauvegarde s'il n'existe pas
if (-not (Test-Path $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir | Out-Null
}

# Générer le nom du fichier de sauvegarde
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupFile = Join-Path $BackupDir "backup_${timestamp}.sql"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Sauvegarde de la base de données" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Host: $Host" -ForegroundColor Gray
Write-Host "Database: $Database" -ForegroundColor Gray
Write-Host "Fichier: $backupFile" -ForegroundColor Gray
Write-Host ""

# Vérifier que pg_dump est disponible
$pgDumpPath = Get-Command pg_dump -ErrorAction SilentlyContinue
if (-not $pgDumpPath) {
    Write-Host "❌ pg_dump n'est pas disponible. Installez PostgreSQL Client." -ForegroundColor Red
    Write-Host "   Télécharger depuis : https://www.postgresql.org/download/windows/" -ForegroundColor Yellow
    exit 1
}

$env:PGPASSWORD = $Password

try {
    Write-Host "✓ Sauvegarde en cours..." -ForegroundColor Green
    
    & pg_dump `
        -h $Host `
        -U $User `
        -p $Port `
        -d $Database `
        --verbose | Out-File -FilePath $backupFile -Encoding UTF8

    # Obtenir la taille du fichier
    $fileSize = (Get-Item $backupFile).Length / 1MB
    $fileSizeFormatted = "{0:N2} MB" -f $fileSize

    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "✅ Sauvegarde complète!" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "Fichier: $backupFile" -ForegroundColor Green
    Write-Host "Taille: $fileSizeFormatted" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Pour restaurer cette sauvegarde :" -ForegroundColor Yellow
    Write-Host "  .\restore-database.ps1 -BackupFile '$backupFile'" -ForegroundColor Gray
    Write-Host ""

    # Optionnel : Créer une archive compressée
    Write-Host "Création d'une archive compressée..." -ForegroundColor Green
    $compressedFile = "$backupFile.zip"
    Compress-Archive -Path $backupFile -DestinationPath $compressedFile -Force
    
    $compressedSize = (Get-Item $compressedFile).Length / 1MB
    $compressedSizeFormatted = "{0:N2} MB" -f $compressedSize
    
    Write-Host "✓ Archive créée: $compressedFile (Taille: $compressedSizeFormatted)" -ForegroundColor Green
    
    # Supprimer le fichier non compressé
    Remove-Item $backupFile
    Write-Host "✓ Fichier original supprimé (gardé uniquement la version compressée)" -ForegroundColor Green
    Write-Host ""

    # Afficher les dernières sauvegardes
    Write-Host "Dernières sauvegardes disponibles :" -ForegroundColor Yellow
    Get-ChildItem $BackupDir -File | Sort-Object LastWriteTime -Descending | Select-Object -First 5 | 
        ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Gray }
    Write-Host ""
}
catch {
    Write-Host "❌ Erreur: $_" -ForegroundColor Red
    exit 1
}
finally {
    $env:PGPASSWORD = ""
}
