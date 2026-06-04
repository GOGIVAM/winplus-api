#!/bin/bash
# ============================================================
# BACKUP - Base de données WinPlus
# Usage: ./backup.sh [nom_optionnel]
# ============================================================

DB_HOST="172.31.8.182"      # IP privée winplus-db
DB_USER="cademi"
DB_NAME="winplus_db"
DB_PASS="Admin001"
BACKUP_DIR="/home/ubuntu/backups"

mkdir -p "$BACKUP_DIR"

if [ -n "$1" ]; then
    FILENAME="winplus_${1}_$(date +%Y%m%d_%H%M%S).sql"
else
    FILENAME="winplus_backup_$(date +%Y%m%d_%H%M%S).sql"
fi

FILEPATH="$BACKUP_DIR/$FILENAME"

echo "================================================"
echo "  SAUVEGARDE BD WinPlus"
echo "================================================"
echo "Date     : $(date '+%Y-%m-%d %H:%M:%S')"
echo "Fichier  : $FILEPATH"
echo ""

PGPASSWORD="$DB_PASS" pg_dump -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" \
    --no-owner --no-acl --clean --if-exists \
    -F p -f "$FILEPATH"

if [ $? -eq 0 ]; then
    SIZE=$(du -h "$FILEPATH" | cut -f1)
    echo "✅ Sauvegarde réussie ! ($SIZE)"
    echo "   → $FILEPATH"

    # Garder seulement les 10 derniers backups
    cd "$BACKUP_DIR"
    ls -t winplus_*.sql 2>/dev/null | tail -n +11 | xargs -r rm --
    TOTAL=$(ls winplus_*.sql 2>/dev/null | wc -l)
    echo "   → $TOTAL backup(s) conservé(s)"
else
    echo "❌ ERREUR lors de la sauvegarde !"
    exit 1
fi

echo "================================================"