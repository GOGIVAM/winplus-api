# WORKFLOW: Configuration BD - Local → GitHub → EC2

## 📋 Étape 1: Configuration LOCAL (votre machine Windows)

### 1.1 Créer le fichier .env.local
```bash
# Dans: backend/database/
# Basé sur le template fourni
cp .env.local.template .env.local
```

**Fichier .env.local** (LOCAL uniquement, JAMAIS sur GitHub):
```
DB_HOST=localhost
DB_PORT=5432
DB_USER=postgres
DB_PASSWORD=postgres
DB_NAME=winplus_db_local
DB_SSLMODE=disable
POSTGRES_BIN=C:\Program Files\PostgreSQL\15\bin
DB_RESET=false
SEED_DATA=true
```

### 1.2 Créer la BD LOCAL
```bash
# Windows (PowerShell)
cd backend/database
.\setup-database-local.bat

# Ou avec PowerShell complet
powershell -ExecutionPolicy Bypass -File setup-database-local.ps1
```

### 1.3 Vérifier la connexion LOCAL
```bash
# PostgreSQL
psql -h localhost -U postgres -d winplus_db_local

# Ou avec psql directement
SELECT COUNT(*) FROM users;
```

---

## 📤 Étape 2: Pusher sur GitHub

**Ce qui se pousse:**
```
backend/database/
├── SCHEMA_AGILE_COMPLET.sql  ✅ COMMITER (schéma universel)
├── setup-database-local.bat   ✅ COMMITER (script local)
├── setup-database-ec2.sh      ✅ COMMITER (script EC2)
├── .env.ec2.template          ✅ COMMITER (template pour EC2)
├── .env.local                 ❌ IGNORER (secrets locaux)
└── seed-data.sql              ✅ COMMITER (optionnel, données test)
```

### 2.1 Vérifier .gitignore
```bash
# Dans: .gitignore racine (ou backend/.gitignore)
*.local
.env.local
.env.ec2
database/.env*
!.env.ec2.template
```

### 2.2 Committer et pousser
```bash
cd /path/to/winplus
git add backend/database/
git commit -m "feat: setup BD avec scripts local + EC2 + templates env"
git push origin main
```

---

## 🚀 Étape 3: Déployer sur EC2

### 3.1 Se connecter à l'EC2 du CODE (pas la BD)
```bash
ssh -i votre-cle.pem ubuntu@<EC2_CODE_IP>
```

### 3.2 Cloner le projet (si pas déjà fait)
```bash
cd ~/projects
git clone https://github.com/votre-repo/winplus.git
cd winplus/backend/database
```

### 3.3 OU mettre à jour depuis main
```bash
cd ~/projects/winplus
git pull origin main
cd backend/database
```

### 3.4 Créer .env.ec2 depuis le template
```bash
cp .env.ec2.template .env.ec2

# Éditer les valeurs si nécessaire
nano .env.ec2
```

**Exemple .env.ec2 sur EC2:**
```
DB_HOST=98.86.67.128
DB_PORT=5432
DB_USER=gogivam
DB_PASSWORD=Admin001
DB_NAME=winplus_db
DB_SSLMODE=require
DB_RESET=false
SEED_DATA=false
DB_BACKUP_ENABLED=true
```

### 3.5 Exécuter le setup
```bash
chmod +x setup-database-ec2.sh
./setup-database-ec2.sh
```

### 3.6 Vérifier la connexion
```bash
# Test simple
PGPASSWORD='Admin001' psql -h 98.86.67.128 -U gogivam -d winplus_db -c "SELECT COUNT(*) FROM users;"

# Ou avec psql
psql -h 98.86.67.128 -U gogivam -d winplus_db
```

---

## 🔧 Modifications SPÉCIFIQUES à l'EC2

Si certaines modifications sont **propres à la version EC2**, créez:

### Option 1: Fichier de patches EC2
```bash
backend/database/
├── SCHEMA_AGILE_COMPLET.sql
├── patches-ec2.sql  ← Modifications spécifiques EC2
└── apply-patches-ec2.sh
```

**patches-ec2.sql** (exemple):
```sql
-- Modifications spécifiques EC2 (sécurité, perfs, etc.)
ALTER TABLE users ADD CONSTRAINT fk_cognito UNIQUE(cognito_id);
CREATE INDEX idx_users_cognito ON users(cognito_id);
-- etc...
```

**apply-patches-ec2.sh:**
```bash
#!/bin/bash
source .env.ec2
PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -f patches-ec2.sql
```

### Option 2: Scripts séparés
```bash
backend/database/
├── schema-base.sql        ← Schéma universel
├── schema-local-ext.sql   ← Extensions locales
└── schema-ec2-ext.sql     ← Extensions EC2
```

Puis dans le script:
```bash
# setup-database-ec2.sh
psql ... -f SCHEMA_AGILE_COMPLET.sql
psql ... -f schema-ec2-ext.sql
```

---

## 📊 Résumé du Workflow

```
LOCAL MACHINE (Windows)
    ↓
    ├─ setup-database-local.bat (crée BD locale)
    ├─ Tester & valider localement
    ├─ Committer: SCHEMA + scripts + .env.template
    └─ git push
        ↓
    GitHub (Central Repository)
        ↓
    EC2 (Instance Code)
        ├─ git pull
        ├─ Copier .env.ec2.template → .env.ec2
        ├─ Éditer .env.ec2 (BD externe IP)
        ├─ ./setup-database-ec2.sh
        └─ Vérifier connexion
            ↓
        EC2 (Instance BD) [optionnel si BD externe]
            └─ Juste accès réseau (groupe sécurité AWS)
```

---

## ✅ Checklist Setup

- [ ] .env.local créé (local, ignoré par Git)
- [ ] BD locale créée et testée
- [ ] SCHEMA_AGILE_COMPLET.sql pushé sur GitHub
- [ ] setup-database-ec2.sh pushé sur GitHub
- [ ] .env.ec2.template pushé sur GitHub
- [ ] .gitignore configuré pour ignorer .env.local et .env.ec2
- [ ] EC2: fichier .env.ec2 créé depuis template
- [ ] EC2: ./setup-database-ec2.sh exécuté
- [ ] EC2: Connexion à la BD testée
- [ ] Groupe de sécurité AWS configuré (port 5432 ouvert)

---

## 🆘 Troubleshooting

### Erreur: "Could not resolve host"
```
❌ PGPASSWORD='...' psql -h 98.86.67.128 ...
```
**Solution:** Groupe sécurité AWS - ajouter règle:
- Type: PostgreSQL
- Port: 5432
- Source: IP de l'EC2 du code

### Erreur: "Connection refused"
```
❌ psql: error: could not connect to server
```
**Solution:** Vérifier que l'EC2 de la BD a PostgreSQL démarré:
```bash
# Sur l'EC2 BD
sudo systemctl status postgresql
sudo systemctl restart postgresql
```

### Erreur: "Permission denied" sur les fichiers .sh
```bash
chmod +x setup-database-ec2.sh
```

---

## 📝 Notes

- **Secrets**: Ne JAMAIS commiter .env.local ou .env.ec2
- **Templates**: Les .template servent de documentation
- **Patches**: Les modifications EC2-spécifiques vont dans patches-ec2.sql
- **Backups**: Active DB_BACKUP_ENABLED=true sur EC2
