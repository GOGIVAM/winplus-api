# 📋 Guide de Déploiement Backend sur EC2 AWS

## Configuration de déploiement
- **Système d'exploitation** : Ubuntu 24.04
- **Base de données** : PostgreSQL externe
- **Host BD** : 98.86.67.128
- **User BD** : gogivam
- **Password BD** : Admin001
- **Database Name** : winplus_db
- **FastApi Port** : 5000
- **.NET Gateway Port** : 5001

---

## **Étape 1 : Mise à jour système**

```bash
sudo apt update
sudo apt upgrade -y
```

---

## **Étape 2 : Installation Python + Git**

```bash
sudo apt install -y python3.12 python3.12-venv python3.12-dev python3-full git
```

Vérifiez :
```bash
python3 --version
```

---

## **Étape 3 : Créer la structure de dossiers**

```bash
mkdir -p ~/projects
cd ~/projects
```

---

## **Étape 4 : Cloner/copier votre projet**

```bash
git clone <votre-repo>
cd reussir/backend
```

*(Ou transférez vos fichiers via SCP/SFTP)*

---

## **Étape 5 : Configuration FastApi (Python)**

### **5a. Créer l'environnement virtuel**
```bash
cd fastapi_api
python3 -m venv venv
source venv/bin/activate
```

### **5b. Mettre à jour pip dans le venv**
```bash
pip install --upgrade pip setuptools wheel
```

### **5c. Installer les outils de compilation (important pour sentencepiece)**
```bash
sudo apt install -y build-essential cmake pkg-config
```

### **5d. Installer les dépendances FastApi**
```bash
pip install -r requirements.txt
```

**Si sentencepiece échoue à compiler :**
```bash
pip install sentencepiece --only-binary :all:
pip install -r requirements.txt
```

### **5e. Créer le fichier .env**
```bash
nano .env
```

Copiez/collez ceci (adapter au besoin) :
```
FLASK_PORT=5000
FLASK_DEBUG=False
DB_TYPE=postgresql
DB_HOST=98.86.67.128
DB_PORT=5432
DB_USER=gogivam
DB_PASSWORD=Admin001
DB_NAME=winplus_db
```

Sauvegardez : `Ctrl+O` → `Entrée` → `Ctrl+X`

### **5f. Désactiver le venv temporairement**
```bash
deactivate
```

---

## **Étape 6 : Installation .NET**

### **6a. Ajouter le repo Microsoft**
```bash
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
```

### **6b. Installer .NET 8**
```bash
sudo apt update
sudo apt install -y dotnet-sdk-8.0
```

Vérifiez :
```bash
dotnet --version
```

---

## **Étape 7 : Configuration .NET Gateway**

### **7a. Retournez au dossier dotnet**
```bash
cd ~/projects/reussir/backend/dotnet
```

### **7b. Modifier appsettings.json**
```bash
nano appsettings.json
```

Assurez-vous que le fichier contient :
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=98.86.67.128;Port=5432;Database=winplus_db;Username=gogivam;Password=Admin001;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "EducationalAI": "Information"
    }
  },
  "AllowedHosts": "*",
  "AIService": {
    "BaseUrl": "http://localhost:5000",
    "TimeoutSeconds": 30,
    "RetryAttempts": 3
  },
  "Swagger": {
    "Title": "Educational AI Gateway API",
    "Version": "v1",
    "Description": "API Gateway pour le service IA éducative - Recommandations et analyse NLP",
    "ContactName": "Support Technique",
    "ContactEmail": "support@educational-ai.example.com"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:4200",
      "http://localhost:8080"
    ]
  }
}
```

Sauvegardez : `Ctrl+O` → `Entrée` → `Ctrl+X`

### **7c. Restaurer et compiler**
```bash
dotnet restore
dotnet build -c Release
```

---

## **Étape 8 : Installation PostgreSQL Client (optionnel mais utile)**

Pour pouvoir se connecter à la BD externe :
```bash
sudo apt install -y postgresql-client
```

Testez la connexion :
```bash
psql -h 98.86.67.128 -U gogivam -d winplus_db
```

Entrez le mot de passe : `Admin001`

Si c'est OK, vous verrez un prompt `winplus_db=>`
Quittez avec : `\q`

---

## **Étape 9 : Installation PostgreSQL Server (optionnel)**

**Seulement si vous voulez une BD PostgreSQL locale EN PLUS de la BD distante :**

```bash
sudo apt install -y postgresql postgresql-contrib
sudo systemctl start postgresql
sudo systemctl enable postgresql
```

*(Sinon, ignorez cette étape)*

---

## **Étape 10 : Création des fichiers systemd (Services)**

### **10a. Service FastApi**
```bash
sudo nano /etc/systemd/system/fastapi-app.service
```

Copiez/collez :
```
[Unit]
Description=FastApi Educational AI Service
After=network.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/home/ubuntu/projects/reussir/backend/fastapi_api
Environment="PATH=/home/ubuntu/projects/reussir/backend/fastapi_api/venv/bin"
ExecStart=/home/ubuntu/projects/reussir/backend/fastapi_api/venv/bin/python app.py
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Sauvegardez : `Ctrl+O` → `Entrée` → `Ctrl+X`

### **10b. Service .NET**
```bash
sudo nano /etc/systemd/system/dotnet-gateway.service
```

Copiez/collez :
```
[Unit]
Description=.NET Educational AI Gateway
After=network.target fastapi-app.service

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/home/ubuntu/projects/reussir/backend/dotnet
ExecStart=/usr/bin/dotnet /home/ubuntu/projects/reussir/backend/dotnet/bin/Release/net8.0/backend.dll
Restart=always
RestartSec=10
Environment="ASPNETCORE_URLS=http://0.0.0.0:5001"

[Install]
WantedBy=multi-user.target
```

Sauvegardez : `Ctrl+O` → `Entrée` → `Ctrl+X`

---

## **Étape 11 : Activer et démarrer les services**

```bash
sudo systemctl daemon-reload
sudo systemctl enable fastapi-app
sudo systemctl enable dotnet-gateway
sudo systemctl start fastapi-app
sudo systemctl start dotnet-gateway
```

Vérifiez qu'ils tournent :
```bash
sudo systemctl status fastapi-app
sudo systemctl status dotnet-gateway
```

---

## **Étape 12 : Configuration du Firewall**

```bash
sudo ufw allow 5000/tcp
sudo ufw allow 5001/tcp
sudo ufw allow 22/tcp
sudo ufw enable
```

---

## **Étape 13 : Configuration AWS Security Group**

Dans la console AWS :
1. Allez à EC2 → Instances
2. Sélectionnez votre instance
3. Cliquez sur le Security Group
4. Modifiez les Inbound Rules
5. Ajoutez :
   - Port 5000 (FastApi) - Source : votre frontend
   - Port 5001 (.NET) - Source : votre frontend
   - Port 5432 (PostgreSQL) - Seulement si accès externe nécessaire
   - Port 22 (SSH) - Source : votre IP

---

## **Étape 14 : Tests finaux**

**Testez FastApi :**
```bash
curl http://localhost:5000/
```

**Testez .NET :**
```bash
curl http://localhost:5001/
```

**Testez la BD :**
```bash
psql -h 98.86.67.128 -U gogivam -d winplus_db -c "SELECT version();"
```

**Vérifiez les logs :**
```bash
sudo journalctl -u fastapi-app -f
sudo journalctl -u dotnet-gateway -f
```

---

## **Étape 15 : Configuration du Nginx (optionnel mais recommandé)**

### **15a. Installation**
```bash
sudo apt install -y nginx
```

### **15b. Créer la config**
```bash
sudo nano /etc/nginx/sites-available/educational-ai
```

Copiez/collez :
```
upstream fastapi_backend {
    server localhost:5000;
}

upstream dotnet_gateway {
    server localhost:5001;
}

server {
    listen 80;
    server_name votre-domaine.com;

    location /api/ai {
        proxy_pass http://fastapi_backend;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /api {
        proxy_pass http://dotnet_gateway;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        proxy_pass http://dotnet_gateway;
        proxy_set_header Host $host;
    }
}
```

Sauvegardez : `Ctrl+O` → `Entrée` → `Ctrl+X`

### **15c. Activer la configuration**
```bash
sudo ln -s /etc/nginx/sites-available/educational-ai /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx
```

---

## **Commandes utiles**

### **Voir les logs en temps réel**
```bash
sudo journalctl -u fastapi-app -f
sudo journalctl -u dotnet-gateway -f
```

### **Redémarrer un service**
```bash
sudo systemctl restart fastapi-app
sudo systemctl restart dotnet-gateway
```

### **Arrêter un service**
```bash
sudo systemctl stop fastapi-app
sudo systemctl stop dotnet-gateway
```

### **Vérifier l'état**
```bash
sudo systemctl status fastapi-app
sudo systemctl status dotnet-gateway
```

### **Vérifier les ports en écoute**
```bash
sudo netstat -tlnp | grep -E '5000|5001'
```

---

## **Troubleshooting**

### **FastApi ne démarre pas**
```bash
cd ~/projects/reussir/backend/fastapi_api
source venv/bin/activate
python app.py
```

### **.NET ne démarre pas**
```bash
cd ~/projects/reussir/backend/dotnet
dotnet run
```

### **Problème de connexion BD**
```bash
psql -h 98.86.67.128 -U gogivam -d winplus_db
```

### **Vérifier que les ports sont ouverts**
```bash
sudo ufw status
```

---

## **Architecture finale**

```
Internet (Client)
    ↓
Nginx (Port 80/443)
    ↓
.NET Gateway (Port 5001)
    ↓
    ├── FastApi Service (Port 5000)
    │   ├── NLP Analyzer
    │   ├── Recommender
    │   └── Predictor
    │
    └── PostgreSQL (98.86.67.128:5432)
        ├── users
        ├── contents
        └── interactions
```

---

## ✅ Déploiement complet !

Votre backend est maintenant déployé et actif sur EC2 ! 🚀

**Prochaines étapes :**
- Déployer le frontend
- Configurer les certificats SSL
- Mettre en place la CI/CD
