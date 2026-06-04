#!/bin/bash
# Script de sauvegarde de la base de données PostgreSQL
# Utilisation : ./backup-database.sh
# Crée un fichier backup_YYYYMMDD_HHMMSS.sql

set -e

# Configuration
DB_HOST="98.86.67.128"
DB_PORT="5432"
DB_USER="gogivam"
DB_PASSWORD="Admin001"
DB_NAME="winplus_db"
BACKUP_DIR="./backups"

# Créer le répertoire de sauvegarde s'il n'existe pas
mkdir -p "$BACKUP_DIR"

# Générer le nom du fichier de sauvegarde
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="$BACKUP_DIR/backup_${TIMESTAMP}.sql"

echo "=========================================="
echo "Sauvegarde de la base de données"
echo "=========================================="
echo "Host: $DB_HOST"
echo "Database: $DB_NAME"
echo "Fichier: $BACKUP_FILE"
echo ""

# Vérifier que pg_dump est disponible
if ! command -v pg_dump &> /dev/null; then
    echo "❌ pg_dump n'est pas installé. Installez postgresql-client:"
    echo "   sudo apt install -y postgresql-client"
    exit 1
fi

# Effectuer la sauvegarde
echo "✓ Sauvegarde en cours..."
PGPASSWORD="$DB_PASSWORD" pg_dump \
    -h "$DB_HOST" \
    -U "$DB_USER" \
    -p "$DB_PORT" \
    -d "$DB_NAME" \
    --verbose \
    > "$BACKUP_FILE"

# Obtenir la taille du fichier
SIZE=$(du -h "$BACKUP_FILE" | cut -f1)

echo ""
echo "=========================================="
echo "✅ Sauvegarde complète!"
echo "=========================================="
echo "Fichier: $BACKUP_FILE"
echo "Taille: $SIZE"
echo ""
echo "Pour restaurer cette sauvegarde :"
echo "  ./restore-database.sh $BACKUP_FILE"
echo ""

# Optionnel : Créer un tarball compressé
echo "Création d'une archive compressée..."
COMPRESSED_FILE="${BACKUP_FILE%.sql}.tar.gz"
tar -czf "$COMPRESSED_FILE" -C "$BACKUP_DIR" "backup_${TIMESTAMP}.sql"
COMPRESSED_SIZE=$(du -h "$COMPRESSED_FILE" | cut -f1)

echo "✓ Archive créée: $COMPRESSED_FILE (Taille: $COMPRESSED_SIZE)"
echo ""

# Optionnel : Supprimer le fichier non compressé
rm "$BACKUP_FILE"
echo "✓ Fichier original supprimé (gardé uniquement la version compressée)"
echo ""

# Afficher les dernières sauvegardes
echo "Dernières sauvegardes disponibles :"
ls -lh "$BACKUP_DIR" | tail -5
echo ""
