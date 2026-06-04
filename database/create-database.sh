#!/bin/bash
# Script de création de la base de données PostgreSQL locale
# Utilisation : ./create-database.sh

set -e

# Configuration
DB_HOST="localhost"
DB_PORT="5432"
DB_USER="postgres"
DB_PASSWORD="postgres"
DB_NAME="winplus_db"

echo "=========================================="
echo "Création de la base de données PostgreSQL"
echo "=========================================="
echo "Host: $DB_HOST"
echo "User: $DB_USER"
echo "Database: $DB_NAME"
echo ""

# Vérifier que PostgreSQL est installé
if ! command -v sudo &> /dev/null; then
    echo "❌ sudo n'est pas disponible"
    exit 1
fi

# Vérifier que PostgreSQL Server est installé et en cours d'exécution
if ! sudo systemctl is-active --quiet postgresql; then
    echo "⚠️  PostgreSQL n'est pas en cours d'exécution, installation en cours..."
    sudo apt install -y postgresql postgresql-contrib
    sudo systemctl start postgresql
    sudo systemctl enable postgresql
fi

# Créer la base de données
echo "✓ Création de la base de données..."
sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname = '$DB_NAME'" | grep -q 1 || \
sudo -u postgres psql -c "CREATE DATABASE $DB_NAME;"

echo "✓ Exécution du schéma SQL..."
# Exécuter le schéma SQL
sudo -u postgres psql -d "$DB_NAME" -f schema.sql

echo ""
echo "=========================================="
echo "✅ Base de données créée avec succès!"
echo "=========================================="
echo ""
echo "Pour se connecter à la base de données :"
echo "  sudo -u postgres psql -d $DB_NAME"
echo ""
echo "Ou depuis l'application :"
echo "  postgresql://postgres:postgres@localhost:5432/$DB_NAME"
echo ""
