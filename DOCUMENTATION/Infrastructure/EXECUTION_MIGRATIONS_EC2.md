# 🚀 Guide d'Exécution des Migrations sur EC2

Date: 3 février 2026

---

## 📍 Situation Actuelle

✅ **Backend, Frontend et FastApi sur le même EC2**: `44.200.166.163`
✅ **PostgreSQL sur EC2 privée**: `172.31.20.230`
✅ **Configuration Cognito**: Complète et fonctionnelle

---

## ⚡ Vérification Rapide (5 minutes)

### Étape 1: Vérifier que le Backend est en cours d'exécution

```bash
# SSH sur EC2
ssh -i your-key.pem ubuntu@44.200.166.163

# Vérifier les processus
ps aux | grep dotnet
ps aux | grep fastapi
ps aux | grep node

# Ou avec Docker (si utilisé)
docker ps

# Vérifier les ports
netstat -tulpn | grep -E '5001|5173|5000'
```

### Étape 2: Vérifier la Connexion à PostgreSQL

```bash
# Tester la connexion
psql -h 172.31.20.230 -U gogivam -d winplus_db -c "SELECT COUNT(*) FROM \"Users\";"

# Résultat attendu:
# count
# -------
#  (nombre de users)
```

### Étape 3: Vérifier l'État des Migrations

```bash
# Aller dans le dossier backend
cd /backend/dotnet  # Adapter le chemin réel

# Si utilisant Docker
docker exec <backend-container> dotnet ef migrations list

# Si sur la machine directement
dotnet ef migrations list

# Résultat attendu: Toutes les migrations marquées ✓ Applied
```

---

## 🔄 Processus Complet de Migration

### Option A: Via Docker (Recommandé pour Production)

#### A1: Créer/Mettre à jour l'Image

```bash
# Dans le répertoire du backend
cd /backend/dotnet

# Build l'image Docker
docker build -f Dockerfile.aws -t winplus-backend:latest .

# Tag pour ECR (si utilisé)
docker tag winplus-backend:latest 123456789.dkr.ecr.us-east-1.amazonaws.com/winplus-backend:latest
```

#### A2: Exécuter les Migrations

```bash
# Créer un conteneur temporaire pour les migrations
docker run --rm \
  --network host \
  -e ConnectionStrings__DefaultConnection="Host=172.31.20.230;Port=5432;Database=winplus_db;Username=gogivam;Password=Admin001;SslMode=Require;" \
  -e AWS__Region="us-east-1" \
  -e AWS__UserPoolId="us-east-1_3vDfozXgb" \
  -e AWS__UserPoolClientId="3gcav7h9ruq9duuf7bv44ll1a8" \
  winplus-backend:latest \
  dotnet ef database update

# Résultat attendu:
# Build started...
# ...
# Done.
```

#### A3: Lancer le Conteneur Backend

```bash
# Arrêter l'ancien conteneur (si existe)
docker stop winplus-backend || true

# Lancer le nouveau
docker run -d \
  --name winplus-backend \
  --network host \
  -e ConnectionStrings__DefaultConnection="Host=172.31.20.230;Port=5432;Database=winplus_db;Username=gogivam;Password=Admin001;SslMode=Require;" \
  -e AWS__Region="us-east-1" \
  -e AWS__UserPoolId="us-east-1_3vDfozXgb" \
  -e AWS__UserPoolClientId="3gcav7h9ruq9duuf7bv44ll1a8" \
  winplus-backend:latest

# Vérifier les logs
docker logs -f winplus-backend
```

---

### Option B: Directement sur le Serveur (.NET SDK)

#### B1: Installer .NET SDK (si non installé)

```bash
# Vérifier si installé
dotnet --version

# Si non installé, télécharger et installer
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Ajouter au PATH
export PATH=$PATH:$HOME/.dotnet
echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc
```

#### B2: Cloner/Mettre à jour le Repository

```bash
# Si premier déploiement
git clone https://votre-repo.git /app/backend
cd /app/backend

# Ou si déjà cloné
cd /app/backend
git pull origin main
```

#### B3: Exécuter les Migrations

```bash
cd /app/backend/dotnet

# Restaurer les dépendances
dotnet restore

# Exécuter les migrations
dotnet ef database update

# Résultat attendu:
# Build started...
# Build succeeded.
# Done.
```

#### B4: Lancer le Backend

```bash
# En arrière-plan
nohup dotnet run --configuration Release > backend.log 2>&1 &

# Ou avec systemd (production)
sudo systemctl restart winplus-backend

# Vérifier
ps aux | grep dotnet
tail -f backend.log
```

---

## 📋 Checklist d'Exécution

- [ ] SSH sur EC2
- [ ] Vérifier les processus en cours
- [ ] Tester connexion PostgreSQL
- [ ] Vérifier l'état des migrations
- [ ] Choisir Option A (Docker) ou Option B (.NET SDK)
- [ ] Exécuter les migrations
- [ ] Lancer le backend
- [ ] Tester les endpoints API
- [ ] Vérifier les logs
- [ ] Tester l'authentification Cognito

---

## 🧪 Tests Post-Migration

### Test 1: Vérifier la Connexion BD

```bash
# SSH sur EC2
ssh -i your-key.pem ubuntu@44.200.166.163

# Vérifier que les tables existent
psql -h 172.31.20.230 -U gogivam -d winplus_db -c "\dt"

# Résultat attendu: Toutes les tables listées (Users, Orders, Subjects, etc.)
```

### Test 2: Vérifier les Endpoints API

```bash
# Test Health Check
curl -X GET http://44.200.166.163:5001/health

# Résultat attendu: 200 OK

# Test Swagger
curl -X GET http://44.200.166.163:5001/swagger/index.html

# Résultat attendu: Page Swagger chargée
```

### Test 3: Tester l'Authentification

```bash
# Test Signup
curl -X POST http://44.200.166.163:5001/api/auth/signup \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "Jean",
    "lastName": "Dupont",
    "email": "jean@example.com",
    "password": "SecurePass123!"
  }'

# Résultat attendu: 200 avec message de confirmation

# Test Login
curl -X POST http://44.200.166.163:5001/api/auth/signin \
  -H "Content-Type: application/json" \
  -d '{
    "email": "jean@example.com",
    "password": "SecurePass123!"
  }'

# Résultat attendu: 200 avec tokens JWT
```

### Test 4: Tester les Données Utilisateur

```bash
# En utilisant le token obtenu du login
TOKEN="<token_from_login_response>"

# Récupérer le profil
curl -X GET http://44.200.166.163:5001/api/auth/profile \
  -H "Authorization: Bearer $TOKEN"

# Résultat attendu: 200 avec les données utilisateur
```

---

## 🆘 Dépannage

### Problème 1: Migration échoue - "Connection refused"

```bash
# Vérifier la connectivité PostgreSQL
telnet 172.31.20.230 5432

# Si refused, vérifier les Security Groups AWS:
# - Entrante: Port 5432 depuis le SG de l'EC2
# - Sortante: Tous les ports (par défaut OK)

# Vérifier les credentials
psql -h 172.31.20.230 -U gogivam -W  # Entrer le password
```

### Problème 2: Migration échoue - "Invalid UTF8"

```bash
# Relancer avec UTF8
export PGCLIENTENCODING=UTF8
dotnet ef database update
```

### Problème 3: Migration échoue - "Concurrence Issue"

```bash
# Arrêter tous les processus .NET
pkill -f "dotnet"

# Attendre 5 secondes
sleep 5

# Relancer la migration
dotnet ef database update
```

### Problème 4: Backend démarre mais Cognito ne fonctionne pas

```bash
# Vérifier les logs
docker logs winplus-backend | grep -i cognito
tail -f backend.log | grep -i cognito

# Résultats attendus:
# "JWT Authentication: Production mode with AWS Cognito JWKS"
# "Cognito Configuration - Region: us-east-1, UserPoolId: us-east-1_3vDfozXgb"
```

### Problème 5: Les tokens JWT ne sont pas validés

```bash
# Vérifier la configuration dans appsettings.Production.json
cat appsettings.Production.json | grep -A 5 "JWT"

# Résultats attendus:
# "Issuer": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_3vDfozXgb"
# "Audience": "3gcav7h9ruq9duuf7bv44ll1a8"

# Redémarrer le backend avec les bonnes variables d'env
```

---

## 📊 Vérification de l'État Final

### Tous les Points de Contrôle

```
✅ PostgreSQL Accessible
├─ Test: psql -h 172.31.20.230 -U gogivam -d winplus_db
├─ Port: 5432 ouvert depuis EC2
└─ Password: Admin001

✅ Migrations Appliquées
├─ Test: dotnet ef migrations list
├─ Dernière: 20260202_AddChatbotTables
└─ État: Toutes marquées ✓ Applied

✅ Backend Démarré
├─ Test: curl http://44.200.166.163:5001/health
├─ Port: 5001
└─ Env: Production

✅ Configuration Cognito
├─ User Pool ID: us-east-1_3vDfozXgb
├─ Client ID: 3gcav7h9ruq9duuf7bv44ll1a8
└─ Region: us-east-1

✅ Authentification Fonctionnelle
├─ Signup: Email envoyé
├─ Confirm: Code vérifié
├─ Login: Tokens JWT reçus
└─ API: Authorization header validé

✅ Frontend Connecté
├─ Callback: /auth/callback fonctionnel
├─ Token Storage: localStorage OK
└─ API Calls: Authorization header ajouté
```

---

## 🚀 Commandes Rapides (Copier-Coller)

### Docker (Option A - Recommandé)

```bash
# Tout en une seule commande
cd /backend/dotnet && \
docker build -f Dockerfile.aws -t winplus-backend:latest . && \
docker run --rm --network host \
  -e ConnectionStrings__DefaultConnection="Host=172.31.20.230;Port=5432;Database=winplus_db;Username=gogivam;Password=Admin001;SslMode=Require;" \
  -e AWS__Region="us-east-1" \
  -e AWS__UserPoolId="us-east-1_3vDfozXgb" \
  -e AWS__UserPoolClientId="3gcav7h9ruq9duuf7bv44ll1a8" \
  winplus-backend:latest \
  dotnet ef database update && \
docker stop winplus-backend || true && \
docker run -d --name winplus-backend --network host \
  -e ConnectionStrings__DefaultConnection="Host=172.31.20.230;Port=5432;Database=winplus_db;Username=gogivam;Password=Admin001;SslMode=Require;" \
  -e AWS__Region="us-east-1" \
  -e AWS__UserPoolId="us-east-1_3vDfozXgb" \
  -e AWS__UserPoolClientId="3gcav7h9ruq9duuf7bv44ll1a8" \
  winplus-backend:latest && \
echo "✅ Backend démarré!"
```

### .NET SDK Direct (Option B)

```bash
# Tout en une seule commande
cd /app/backend/dotnet && \
dotnet restore && \
dotnet ef database update && \
nohup dotnet run --configuration Release > backend.log 2>&1 & && \
echo "✅ Backend démarré!"
```

---

## ✅ Conclusion

Votre système est **prêt pour les migrations**. Choisissez:

- **Docker** si vous utilisez une orchestration (plus flexible)
- **.NET SDK** si vous déployez directement sur la machine

Les deux options mèneront au même résultat: ✅ Backend opérationnel avec Cognito validé.

Avez-vous besoin d'aide pour exécuter l'une de ces étapes?
