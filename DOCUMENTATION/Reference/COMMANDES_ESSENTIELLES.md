# ⚡ COMMANDES ESSENTIELLES

**Référence rapide - Copier/coller directement**

---

## 🔧 BACKEND ESSENTIAL COMMANDS

### Terminal 1 - Démarrage Backend
```powershell
# Navigation
cd M:\win\winplus\backend\dotnet

# Première fois uniquement
dotnet restore

# À chaque démarrage
dotnet build
dotnet run

# ✅ Succès = "Application started" + "listening on http://localhost:5001"
```

### Test health backend (nouveau Terminal)
```powershell
# Test simple
curl http://localhost:5001/api/health

# Test complet (ready)
curl http://localhost:5001/api/health/ready

# Test spécifiques
curl http://localhost:5001/api/health/db
curl http://localhost:5001/api/health/fastapi
curl http://localhost:5001/api/health/cognito
curl http://localhost:5001/api/health/diagnostics
```

### Test categories endpoint
```powershell
curl http://localhost:5001/api/categories
curl http://localhost:5001/api/categories/exams
curl http://localhost:5001/api/categories/subjects
curl http://localhost:5001/api/categories/difficulties
curl http://localhost:5001/api/categories/years
curl http://localhost:5001/api/categories/filters
```

### Test subjects endpoint
```powershell
curl "http://localhost:5001/api/subjects?page=1&pageSize=10"
curl "http://localhost:5001/api/subjects/popular?limit=5"
curl "http://localhost:5001/api/subjects/featured?limit=5"
curl "http://localhost:5001/api/subjects/recent?limit=5"
curl "http://localhost:5001/api/subjects/by-category/math?page=1&pageSize=10"
```

---

## 🎨 FRONTEND ESSENTIAL COMMANDS

### Terminal 2 - Démarrage Frontend
```powershell
# Navigation
cd M:\win\winplus\frontend

# Première fois uniquement
npm install

# À chaque démarrage
npm run dev

# ✅ Succès = "VITE v... ready in ... ms"
# ✅ URL: http://localhost:5173
```

### Frontend troubleshoot
```powershell
# Nettoyer cache
rm -r node_modules
npm install

# Vérifier ports
netstat -ano | findstr :5173    # Frontend
netstat -ano | findstr :5001    # Backend
```

---

## 🔍 DEBUGGING QUICK COMMANDS

### Vérifier ports utilisés
```powershell
# Port 5001 (Backend)
netstat -ano | findstr :5001
# Si réponse = "LISTENING" → ok
# Si réponse vide = pas en écoute

# Port 5173 (Frontend)  
netstat -ano | findstr :5173

# Tuer process si besoin
Get-Process -Id <PID> | Stop-Process -Force
```

### Vérifier PostgreSQL
```powershell
# Tester connexion
psql -U postgres -h localhost -c "SELECT 1"

# Créer base si manque
psql -U postgres -c "CREATE DATABASE winplus"

# Lister bases
psql -U postgres -l
```

### Vérifier configuration
```powershell
# Afficher appsettings
cat backend/dotnet/appsettings.json | jq .

# Vérifier JWT section
cat backend/dotnet/appsettings.json | jq .Jwt

# Vérifier AIService config
cat backend/dotnet/appsettings.json | jq .AIService
```

### Logs backend
```powershell
# Sauvegarder logs pendant exécution
dotnet run 2>&1 | Tee-Object -FilePath "backend_logs.txt"

# Rechercher erreur spécifique
Get-Content backend_logs.txt | Select-String "Error" -Context 2

# Dernières lignes
Get-Content backend_logs.txt -Tail 50
```

---

## 🚀 WORKFLOW COMPLET (5 MINUTES)

### 1. Terminal 1 - Backend
```powershell
cd M:\win\winplus\backend\dotnet
dotnet run
# Attendre "listening on http://localhost:5001"
```

### 2. Terminal 2 - Frontend
```powershell
cd M:\win\winplus\frontend
npm run dev
# Attendre "ready in ... ms"
```

### 3. Terminal 3 - Tester
```powershell
# Backend health
curl http://localhost:5001/api/health

# Frontend accessible
curl http://localhost:5173

# Categories endpoint
curl http://localhost:5001/api/categories
```

### 4. Browser
```
http://localhost:5173
```

✅ **Tout fonctionne!**

---

## 🔴 ERREUR HANDLING RAPIDE

### "Address already in use" - Port 5001/5173
```powershell
# Trouver process
$proc = Get-Process | Where-Object { $_.ProcessName -like "*node*" -or $_.ProcessName -like "*dotnet*" }

# Tuer si besoin
Stop-Process -Id $proc.Id -Force

# Relancer
cd backend/dotnet && dotnet run
```

### "Connection refused" - Backend pas accessible
```powershell
# Vérifier running
curl http://localhost:5001/api/health

# Si erreur = pas lancé
cd M:\win\winplus\backend\dotnet
dotnet run

# Vérifier logs pour erreurs
# Voir logs section ci-dessus
```

### "CORS blocked" - Frontend → Backend erreur
```powershell
# Vérifier CORS config
cat backend/dotnet/appsettings.json | jq .Cors

# Vérifier URL
# Frontend doit appeler http://localhost:5001 (HTTP, pas HTTPS)
```

### "DatabaseException" - PostgreSQL erreur
```powershell
# Vérifier PostgreSQL running
psql -U postgres -c "SELECT 1"

# Vérifier ConnectionString
cat backend/dotnet/appsettings.Development.json | jq .ConnectionStrings

# Créer DB si manque
psql -U postgres -c "CREATE DATABASE winplus"
```

---

## 📊 QUICK STATUS CHECK

### Tester rapidement tout
```powershell
Write-Host "🔍 Checking Backend..." -ForegroundColor Blue
$backend = Invoke-WebRequest http://localhost:5001/api/health -ErrorAction SilentlyContinue
if ($backend.StatusCode -eq 200) { Write-Host "✅ Backend OK" } else { Write-Host "❌ Backend DOWN" }

Write-Host "`n🔍 Checking Frontend..." -ForegroundColor Blue
$frontend = Invoke-WebRequest http://localhost:5173 -ErrorAction SilentlyContinue
if ($frontend.StatusCode -eq 200) { Write-Host "✅ Frontend OK" } else { Write-Host "❌ Frontend DOWN" }

Write-Host "`n🔍 Checking Categories..." -ForegroundColor Blue
$categories = Invoke-WebRequest http://localhost:5001/api/categories -ErrorAction SilentlyContinue
if ($categories.StatusCode -eq 200) { Write-Host "✅ Categories OK" } else { Write-Host "❌ Categories ERROR" }

Write-Host "`n✨ All checks complete!" -ForegroundColor Green
```

---

## 💾 GIT COMMANDS (si utilisé)

```powershell
# Voir changements
git status

# Voir diff
git diff backend/dotnet/

# Ajouter fichiers
git add .

# Commit
git commit -m "Corrections audit objectifs 2-7"

# Push
git push origin main

# Rollback si besoin
git reset --hard HEAD
```

---

## 📝 CONFIGURATION FICHIERS

### Fichiers à connaître
```
M:\win\winplus\
├── backend\dotnet\
│   ├── appsettings.json (config base)
│   ├── appsettings.Development.json (dev override)
│   ├── appsettings.Production.json (prod override)
│   ├── Services\FastApiClient.cs (FastApi resilience)
│   ├── Controllers\HealthController.cs (health checks)
│   ├── Controllers\CategoriesController.cs (categories)
│   ├── Controllers\SubjectsController.cs (subjects)
│   └── Program.cs (DI registration)
└── frontend\src\
    ├── services\catalogService.ts (API client)
    └── pages\CatalogPage.tsx (main page)
```

### Éditer config
```powershell
# Backend config
notepad M:\win\winplus\backend\dotnet\appsettings.json

# Frontend env
notepad M:\win\winplus\frontend\.env

# Relancer pour appliquer
# (dotnet run reload automatique)
# (npm run dev reload automatique Vite)
```

---

## 🔗 URLS DE RÉFÉRENCE

### Endpoints Backend
```
Base: http://localhost:5001/api/

Health:
- GET /health
- GET /health/ready
- GET /health/db
- GET /health/fastapi
- GET /health/cognito
- GET /health/diagnostics

Categories:
- GET /categories
- GET /categories/exams
- GET /categories/subjects
- GET /categories/difficulties
- GET /categories/years
- GET /categories/filters

Subjects:
- GET /subjects?page=1&pageSize=10
- GET /subjects/popular?limit=10
- GET /subjects/featured?limit=10
- GET /subjects/recent?limit=10
- GET /subjects/by-category/{name}
- GET /subjects/search?q=...
```

### Frontend
```
Base: http://localhost:5173/

Pages:
- / (home)
- /catalog (courses)
- /profile (user)
```

---

## 🎯 DAILY CHECKLIST

**Au démarrage du jour:**
```powershell
# 1. Backend
cd M:\win\winplus\backend\dotnet && dotnet run &

# 2. Frontend
cd M:\win\winplus\frontend && npm run dev &

# 3. Vérifier
curl http://localhost:5001/api/health
curl http://localhost:5173

# ✅ Si 200 OK → prêt à développer
```

**À la fin de la journée:**
```powershell
# 1. Arrêter servers (Ctrl+C dans chaque terminal)

# 2. Commit si changements
git add .
git commit -m "Daily work - [description]"
git push

# 3. ✅ Done!
```

---

## ⚡ TRICKS & TIPS

### Alias PowerShell (à ajouter au profil)
```powershell
# Profile: $PROFILE

# Backend quick start
function Start-Backend {
    cd M:\win\winplus\backend\dotnet
    dotnet run
}

# Frontend quick start
function Start-Frontend {
    cd M:\win\winplus\frontend
    npm run dev
}

# Health check
function Check-Health {
    Write-Host "Backend:" -ForegroundColor Blue
    curl http://localhost:5001/api/health
    Write-Host "`nFrontend:" -ForegroundColor Blue
    curl http://localhost:5173
}
```

### Utiliser aliases
```powershell
# Après ajout au profil
Start-Backend
Start-Frontend  
Check-Health
```

---

## 📱 WINDOWS TERMINAL MULTI-TAB

```powershell
# Démarrer VS Code avec bon répertoire
code M:\win\winplus

# Alt+Shift+1 = nouveau tab
# Alt+Shift+D = split horizontal

# Tab 1: Backend
cd backend\dotnet
dotnet run

# Tab 2: Frontend  
cd frontend
npm run dev

# Tab 3: Testing/Debugging
# Libre pour tests

# Vérifier tout en même temps!
```

---

**À mémoriser:** 
- `cd M:\win\winplus\backend\dotnet && dotnet run` 
- `cd M:\win\winplus\frontend && npm run dev`
- `curl http://localhost:5001/api/health`
- Browser: `http://localhost:5173`

**C'est tout ce qu'il faut! 🚀**
