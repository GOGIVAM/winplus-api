#!/bin/bash
# ============================================================
# RESTORE - Base de données WinPlus
# Usage: ./restore.sh [fichier.sql]
# ============================================================

DB_HOST="172.31.8.182"      # IP privée winplus-db
DB_USER="cademi"
DB_NAME="winplus_db"
DB_PASS="Admin001"
BACKUP_DIR="/home/ubuntu/backups"

echo "================================================"
echo "  RESTAURATION BD WinPlus"
echo "================================================"

if [ -z "$1" ]; then
    echo ""
    echo "Backups disponibles :"
    echo "---------------------"

    if [ ! -d "$BACKUP_DIR" ] || [ -z "$(ls $BACKUP_DIR/winplus_*.sql 2>/dev/null)" ]; then
        echo "  Aucun backup trouvé dans $BACKUP_DIR"
        echo ""
        echo "Usage: ./restore.sh <fichier.sql>"
        exit 1
    fi

    i=1
    for f in $(ls -t "$BACKUP_DIR"/winplus_*.sql 2>/dev/null); do
        SIZE=$(du -h "$f" | cut -f1)
        echo "  [$i] $(basename $f)  ($SIZE)"
        i=$((i+1))
    done

    echo ""
    echo "Usage: ./restore.sh <nom_du_fichier>.sql"
    exit 0
fi

if [ -f "$1" ]; then
    FILEPATH="$1"
elif [ -f "$BACKUP_DIR/$1" ]; then
    FILEPATH="$BACKUP_DIR/$1"
else
    echo "❌ Fichier introuvable : $1"
    exit 1
fi

echo "Fichier  : $FILEPATH"
echo "Base     : $DB_NAME"
echo "Date     : $(date '+%Y-%m-%d %H:%M:%S')"
echo ""

read -p "⚠️  ATTENTION: Cela va ÉCRASER la base actuelle. Continuer ? (oui/non) : " CONFIRM
if [ "$CONFIRM" != "oui" ]; then
    echo "Restauration annulée."
    exit 0
fi

echo ""
echo "📦 Sauvegarde de sécurité avant restauration..."
SAFETY_FILE="$BACKUP_DIR/winplus_avant_restore_$(date +%Y%m%d_%H%M%S).sql"
PGPASSWORD="$DB_PASS" pg_dump -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" \
    --no-owner --no-acl -F p -f "$SAFETY_FILE" 2>/dev/null

if [ $? -eq 0 ]; then
    echo "   ✅ Sauvegarde de sécurité : $(basename $SAFETY_FILE)"
else
    echo "   ⚠️  Sauvegarde de sécurité échouée (on continue quand même)"
fi

echo ""
echo "🔄 Restauration en cours..."
PGPASSWORD="$DB_PASS" psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" \
    -f "$FILEPATH" --quiet 2>&1 | grep -i "error" | head -5

if [ ${PIPESTATUS[0]} -eq 0 ]; then
    echo ""
    echo "✅ Restauration terminée !"
    echo ""
    echo "📊 Vérification :"
    PGPASSWORD="$DB_PASS" psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -c "
    SELECT 'Users' as t, COUNT(*) FROM \"Users\"
    UNION ALL SELECT 'Subjects', COUNT(*) FROM \"Subjects\"
    UNION ALL SELECT 'Enrollments', COUNT(*) FROM \"Enrollments\"
    UNION ALL SELECT 'Orders', COUNT(*) FROM \"Orders\"
    UNION ALL SELECT 'PricingPlans', COUNT(*) FROM \"PricingPlans\"
    ORDER BY t;"
else
    echo ""
    echo "❌ Des erreurs sont survenues."
    echo "   Backup de sécurité disponible : $SAFETY_FILE"
fi

echo "================================================"