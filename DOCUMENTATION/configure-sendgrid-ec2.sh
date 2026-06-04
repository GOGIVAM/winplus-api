#!/bin/bash
# Script pour configurer SendGrid sur EC2
# Usage: ./configure-sendgrid-ec2.sh YOUR_API_KEY

if [ $# -lt 1 ]; then
    echo "❌ Usage: $0 <SENDGRID_API_KEY>"
    echo "Example: $0 SG.xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
    exit 1
fi

API_KEY=$1
FROM_EMAIL="${2:-support@gogivam.com}"
FROM_NAME="${3:-WinPlus Support}"

echo "🔧 Configuration SendGrid sur EC2"
echo "===================================="
echo "API Key: ${API_KEY:0:20}..."
echo "From Email: $FROM_EMAIL"
echo "From Name: $FROM_NAME"
echo ""

# Étape 1 : Arrêter le service
echo "⏹️  Arrêt du service winplus-backend..."
sudo systemctl stop winplus-backend
echo "✅ Service arrêté"
echo ""

# Étape 2 : Configurer les variables d'environnement dans systemd
echo "📝 Configuration des variables d'environnement..."
SYSTEMD_FILE="/etc/systemd/system/winplus-backend.service"

# Lire le fichier et vérifier s'il existe
if [ ! -f "$SYSTEMD_FILE" ]; then
    echo "❌ Erreur: Le fichier $SYSTEMD_FILE n'existe pas"
    exit 1
fi

# Backup du fichier original
sudo cp "$SYSTEMD_FILE" "$SYSTEMD_FILE.backup"
echo "✅ Backup créé: $SYSTEMD_FILE.backup"

# Ajouter/Remplacer les variables d'environnement
sudo sed -i "/Environment=\"SENDGRID.*$/d" "$SYSTEMD_FILE"
sudo sed -i "/\[Service\]/a Environment=\"SENDGRID_API_KEY=$API_KEY\"\\nEnvironment=\"SENDGRID_FROM_EMAIL=$FROM_EMAIL\"\\nEnvironment=\"SENDGRID_FROM_NAME=$FROM_NAME\"" "$SYSTEMD_FILE"

echo "✅ Variables d'environnement configurées"
echo ""

# Étape 3 : Recharger systemd
echo "🔄 Rechargement de systemd..."
sudo systemctl daemon-reload
echo "✅ Systemd rechargé"
echo ""

# Étape 4 : Démarrer le service
echo "▶️  Démarrage de winplus-backend..."
sudo systemctl start winplus-backend
sleep 2
echo "✅ Service démarré"
echo ""

# Étape 5 : Vérifier le statut
echo "📊 Statut du service:"
sudo systemctl status winplus-backend --no-pager
echo ""

# Étape 6 : Vérifier les logs
echo "📋 Vérification des logs (10 dernières lignes)..."
echo "Appuie sur Ctrl+C pour arrêter la surveillance"
echo ""
sudo journalctl -u winplus-backend -n 20 --no-pager

echo ""
echo "✅ Configuration terminée !"
echo "===================================="
echo ""
echo "📧 Pour tester :"
echo "  1. Fais un signup sur l'application"
echo "  2. Vérifiez que l'email de vérification est reçu"
echo "  3. Regardez les logs si tu as des problèmes:"
echo ""
echo "     sudo journalctl -u winplus-backend -f"
echo ""
