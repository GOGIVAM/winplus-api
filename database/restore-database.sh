#!/bin/bash
# Script de restauration de la base de données PostgreSQL
# Utilisation : ./restore-database.sh <fichier_backup>
# Exemple : ./restore-database.sh backups/backup_20240105_120000.sql

set -e

# Configuration
DB_HOST="98.86.67.128"
DB_PORT="5432"
DB_USER="gogivam"
DB_PASSWORD="Admin001"
DB_NAME="winplus_db"

# Vérifier les arguments
if [ $# -ne 1 ]; then
    echo "❌ Utilisation: $0 <fichier_backup>"
    echo "Exemple: $0 backups/backup_20240105_120000.sql"
    echo ""
    echo "Fichiers disponibles :"
    ls -lh ./backups/ 2>/dev/null || echo "Aucun fichier de sauvegarde trouvé."
    exit 1
fi

BACKUP_FILE="$1"

# Vérifier que le fichier existe
if [ ! -f "$BACKUP_FILE" ]; then
    echo "❌ Fichier non trouvé: $BACKUP_FILE"
    exit 1
fi

# Si le fichier est en .tar.gz, le décompresser
if [[ "$BACKUP_FILE" == *.tar.gz ]]; then
    echo "✓ Décompression du fichier..."
    TEMP_DIR=$(mktemp -d)
    tar -xzf "$BACKUP_FILE" -C "$TEMP_DIR"
    BACKUP_FILE="$TEMP_DIR/$(basename "$BACKUP_FILE" .tar.gz).sql"
    CLEANUP_TEMP=true
else
    CLEANUP_TEMP=false
fi

echo "=========================================="
echo "Restauration de la base de données"
echo "=========================================="
echo "Host: $DB_HOST"
echo "Database: $DB_NAME"
echo "Fichier: $BACKUP_FILE"
echo ""

# Vérifier que psql est disponible
if ! command -v psql &> /dev/null; then
    echo "❌ psql n'est pas installé. Installez postgresql-client:"
    echo "   sudo apt install -y postgresql-client"
    exit 1
fi

# ATTENTION : Demander confirmation
echo "⚠️  ATTENTION: Cela supprimera toutes les données actuelles!"
echo "Êtes-vous sûr de vouloir continuer? (tapez 'OUI' pour confirmer)"
read -r CONFIRMATION

if [ "$CONFIRMATION" != "OUI" ]; then
    echo "❌ Restauration annulée."
    [ "$CLEANUP_TEMP" = true ] && rm -rf "$TEMP_DIR"
    exit 0
fi

# Supprimer la base de données existante
echo "✓ Suppression de la base de données existante..."
PGPASSWORD="$DB_PASSWORD" psql \
    -h "$DB_HOST" \
    -U "$DB_USER" \
    -p "$DB_PORT" \
    -tc "DROP DATABASE IF EXISTS $DB_NAME;"

# Créer une nouvelle base de données
echo "✓ Création d'une nouvelle base de données..."
PGPASSWORD="$DB_PASSWORD" psql \
    -h "$DB_HOST" \
    -U "$DB_USER" \
    -p "$DB_PORT" \
    -c "CREATE DATABASE $DB_NAME;"

# Restaurer les données
echo "✓ Restauration des données..."
PGPASSWORD="$DB_PASSWORD" psql \
    -h "$DB_HOST" \
    -U "$DB_USER" \
    -p "$DB_PORT" \
    -d "$DB_NAME" \
    -f "$BACKUP_FILE"

# Nettoyage du fichier temporaire si nécessaire
[ "$CLEANUP_TEMP" = true ] && rm -rf "$TEMP_DIR"

echo ""
echo "=========================================="
echo "✅ Restauration complète!"
echo "=========================================="
echo ""
echo "Vérification de la restauration :"
echo "  psql -h $DB_HOST -U $DB_USER -d $DB_NAME -c \"\\dt\""
echo ""
