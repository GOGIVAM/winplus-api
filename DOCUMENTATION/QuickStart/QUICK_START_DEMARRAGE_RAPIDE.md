# 🚀 QUICK START - DÉMARRAGE RAPIDE WINPLUS

**Objectif:** Lancer l'application complète en 5 minutes

---

## ⏱️ ÉTAPE 1: PRÉPARER ENVIRONMENT (1 min)

### Prérequis:
- ✅ Node.js 18+ installé
- ✅ .NET 8 SDK installé
- ✅ PostgreSQL 15+ running
- ✅ Python 3.9+ (pour FastApi)

### Vérifier installations:
```powershell
node --version        # v18.x.x ou plus
dotnet --version      # .NET 8.x.x ou plus
psql --version        # 15.x ou plus
python --version      # 3.9+ ou plus
```

---

## ⏱️ ÉTAPE 2: LANCER BACKEND (2 min)

### Terminal 1 - Backend ASP.NET:
```powershell
cd M:\win\winplus\backend\dotnet

# Compiler (première fois ou après changements)
dotnet build

# Lancer le serveur
dotnet run

# ✅ Succès = "Application started"
# ✅ URL: http://localhost:5001
```

### Vérifier:
```powershell
# Dans autre terminal, tester health:
curl http://localhost:5001/api/health

# Réponse attendue:
# {"status":"healthy"}
```

---

## ⏱️ ÉTAPE 3: LANCER FRONTEND (2 min)

### Terminal 2 - Frontend React:
```powershell
cd M:\win\winplus\frontend

# Installer dépendances (première fois seulement)
npm install

# Lancer dev server
npm run dev

# ✅ Succès = "VITE ready"
# ✅ URL: http://localhost:5173
```

### Vérifier:
```powershell
# Consulter DevTools (F12) dans browser
# Onglet Network: requêtes vers http://localhost:5001
# Status: 200 OK
```

---

## ⏱️ ÉTAPE 4: VÉRIFIER INTÉGRATION (0 min)

### Ouvrir browser:
```
http://localhost:5173
```

### Points de contrôle:
- [ ] Page load sans erreur
- [ ] Pas de rouge errors en console (F12)
- [ ] Network tab affiche requêtes `/api/categories`
- [ ] Network tab affiche requêtes `/api/subjects`

---

## 🔄 WORKFLOWS RAPIDES

### Développer une nouvelle feature:

1. **Modifier Backend** (C#):
   ```csharp
   // Éditer fichier dans backend/dotnet/
   // dotnet run relance automatiquement (si hot-reload activé)
   ```

2. **Modifier Frontend** (TypeScript/TSX):
   ```typescript
   // Éditer fichier dans frontend/src/
   // Vite relance automatiquement
   ```

3. **Modifier Configuration**:
   ```json
   // Éditer appsettings.json
   // dotnet run relance automatiquement
   ```

---

## 🔍 DEBUGGING RAPIDE

### Backend ne répond pas:
```powershell
# Vérifier sur port 5001:
curl http://localhost:5001/api/health

# Si pas de réponse = server pas démarré
# Vérifier Terminal 1 pour erreurs
```

### Frontend ne load pas:
```powershell
# Vérifier sur port 5173:
curl http://localhost:5173

# Si pas de réponse = dev server pas lancé
# Vérifier Terminal 2 pour erreurs
```

### API 404 Not Found:
```powershell
# Vérifier endpoint exact:
curl "http://localhost:5001/api/categories"

# Si 404 = route mal enregistrée
# Vérifier CategoriesController en backend/dotnet/Controllers/
```

### API 500 Internal Error:
```powershell
# Vérifier logs Terminal 1
# Exception affichée dans output

# Solutions courantes:
# 1. PostgreSQL pas connectée
# 2. Cognito config manquante
# 3. Exception en C# non gérée
```

---

## 📋 CHECKLIST DÉMARRAGE

- [ ] **Backend:**
  - [ ] Terminal 1: `dotnet run` → "Application started"
  - [ ] `curl http://localhost:5001/api/health` → `{"status":"healthy"}`
  - [ ] Pas d'erreurs rouges dans logs

- [ ] **Frontend:**
  - [ ] Terminal 2: `npm run dev` → "VITE ready"
  - [ ] Browser http://localhost:5173 → Page loads
  - [ ] F12 Console: Pas d'erreurs rouges
  - [ ] F12 Network: `/api/` requests → 200 OK

- [ ] **Intégration:**
  - [ ] CatalogPage affiche données
  - [ ] Filtres fonctionnent
  - [ ] Pas de hardcoded data affichée

---

## 🛑 PROBLÈMES COURANTS

### "Address already in use: 5001"
```powershell
# Autre processus utilise port 5001
# Solution:
Get-Process -Port 5001        # Voir quel process
Stop-Process -Id <PID>        # Arrêter
dotnet run                    # Relancer
```

### "Connection refused to localhost:5001"
```powershell
# Backend pas lancé
# Solution:
cd backend/dotnet
dotnet run

# Ou vérifier firewall bloque port 5001
```

### "CORS policy blocked"
```powershell
# Frontend ne peut pas appeler backend
# Solution:
# 1. Backend sur http://localhost:5001 (HTTP, pas HTTPS)
# 2. Frontend VITE_API_URL = http://localhost:5001
# 3. appsettings.json CORS: "http://localhost:5173"
```

### "DatabaseException"
```powershell
# PostgreSQL pas accessible
# Solution:
# 1. Vérifier PostgreSQL running:
#    psql -U postgres -c "SELECT 1"
# 2. Vérifier ConnectionString dans appsettings.Development.json
# 3. Vérifier DB "winplus" existe:
#    psql -U postgres -c "CREATE DATABASE winplus"
```

---

## 📊 PORTS DE RÉFÉRENCE

| Service | Port | URL |
|---------|------|-----|
| Backend ASP.NET | 5001 | http://localhost:5001 |
| Frontend React | 5173 | http://localhost:5173 |
| FastApi AI (local) | 5000 | http://localhost:5000 |
| PostgreSQL | 5432 | localhost:5432 |

---

## 🎯 ENDPOINTS DE TEST

Tester ces URLs directement dans browser ou curl:

```
# Backend Health
http://localhost:5001/api/health

# Categories
http://localhost:5001/api/categories

# Subjects
http://localhost:5001/api/subjects?page=1&pageSize=10

# Popular subjects
http://localhost:5001/api/subjects/popular?limit=5

# Frontend
http://localhost:5173
```

---

## 📝 TEMPLATE LOGS SAUVEGARDE

Pour enregistrer erreurs pour debugging:

```powershell
# Terminal 1 - Sauvegarder logs backend:
dotnet run | Out-File -FilePath "backend_logs.txt" -Append

# Terminal 2 - Sauvegarder logs frontend:
npm run dev 2>&1 | Out-File -FilePath "frontend_logs.txt" -Append
```

---

## 🔗 LIEN RAPIDE VERS DOCS

- **Synthèse corrections**: [SYNTHESE_CORRECTIONS_AUDIT_2-7.md](SYNTHESE_CORRECTIONS_AUDIT_2-7.md)
- **Checklist test complet**: [CHECKLIST_COMPILATION_TESTS.md](CHECKLIST_COMPILATION_TESTS.md)
- **Config appsettings**: `backend/dotnet/appsettings.json`
- **Code API**: `backend/dotnet/Controllers/`
- **Code Frontend**: `frontend/src/pages/CatalogPage.tsx`

---

**Bon démarrage! 🚀** Avez-vous besoin d'aide pour un endpoint particulier?
