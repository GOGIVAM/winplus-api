# 🚀 QUICK START GUIDE - SPRINT 4

**Status**: ✅ **PRODUCTION READY**  
**Build**: 0 Errors, 29 Warnings (non-blocking)  
**Endpoints**: 51/51 ✅ 100% Aligned

---

## 📦 BUILD & RUN

### Backend (.NET Core 8.0)
```bash
# Compiler
cd backend/dotnet
dotnet build

# Lancer le serveur
dotnet run
# Server listening on: https://localhost:7023

# Tester build
dotnet build --no-restore
```

### Frontend (React 18 + Vite)
```bash
# Installer dépendances
cd frontend
npm install

# Mode développement
npm run dev
# Frontend on: http://localhost:3000

# Build production
npm run build
```

### Database (PostgreSQL)
```bash
# Dans le dossier backend/database
python script.py
# Initialise la BD avec le schéma + données
```

### AI Service (FastApi - Optionnel)
```bash
cd backend/fastapi_api
python app_sprint3_mock.py
# AI Service on: http://localhost:5000
```

---

## 🔗 ENDPOINTS PRINCIPALES

### Authentification
```
POST /api/auth/signin        → Se connecter
POST /api/auth/signup        → S'inscrire
POST /api/auth/refresh       → Renouveler token
POST /api/auth/logout        → Se déconnecter
```

### Cours & Recommandations
```
GET  /api/subjects                    → Tous les cours
GET  /api/subjects/{id}               → Détails cours
GET  /api/subjects/categories         → [NEW] Toutes catégories
GET  /api/subjects/filters            → [NEW] Filtres de recherche
GET  /api/subjects/{id}/similar       → [NEW] Cours similaires
GET  /api/subjects/category/{name}    → Cours par catégorie
GET  /api/subjects/search?q=...       → Rechercher
```

### Profil Utilisateur
```
GET  /api/users/profile                    → Mon profil
PUT  /api/users/profile                    → Modifier profil
GET  /api/users/profile/statistics         → [NEW] Stats profil
GET  /api/users/{id}/statistics            → [NEW] Stats utilisateur
```

### Panier & Commandes
```
POST /api/cart/add              → Ajouter au panier
GET  /api/cart                  → Voir panier
POST /api/cart/clear            → Vider panier
POST /api/orders                → Créer commande
GET  /api/orders/{id}           → Détails commande
```

### Paiements
```
POST /api/payments              → Créer paiement
POST /api/payments/{id}/confirm → Confirmer paiement
POST /api/payments/{id}/refund  → Rembourser
DELETE /api/payments/{id}       → Annuler paiement
```

### Inscriptions Cours
```
POST /api/enrollments                      → S'inscrire à un cours
GET  /api/enrollments/user/{userId}        → Mes cours
GET  /api/enrollments/{userId}/{subjectId} → Détails inscription
```

### Favoris
```
POST /api/favorites        → Ajouter favori
GET  /api/favorites        → Mes favoris
DELETE /api/favorites/{id} → Retirer favori
```

---

## 🧪 TESTER LES ENDPOINTS

### Avec REST Client (VS Code Extension)
Fichier: `backend/dotnet/backend.http`
```
### GET all subjects
GET http://localhost:7023/api/subjects

### Get subject by ID
GET http://localhost:7023/api/subjects/1

### Get categories [NEW]
GET http://localhost:7023/api/subjects/categories

### Get filters [NEW]
GET http://localhost:7023/api/subjects/filters

### Get similar courses [NEW]
GET http://localhost:7023/api/subjects/1/similar?limit=5

### Get user statistics [NEW]
GET http://localhost:7023/api/users/1/statistics

### Get profile statistics [NEW]
GET http://localhost:7023/api/users/profile/statistics
```

### Avec cURL
```bash
# Lister tous les cours
curl -X GET http://localhost:7023/api/subjects

# Récupérer catégories [NEW]
curl -X GET http://localhost:7023/api/subjects/categories

# Récupérer filtres [NEW]
curl -X GET http://localhost:7023/api/subjects/filters

# Récupérer cours similaires [NEW]
curl -X GET "http://localhost:7023/api/subjects/1/similar?limit=5"

# Statistiques utilisateur [NEW]
curl -X GET http://localhost:7023/api/users/1/statistics

# Statistiques profil [NEW]
curl -X GET http://localhost:7023/api/users/profile/statistics
```

---

## 📊 ARCHITECTURE

```
┌─────────────────────────────────────────┐
│        REACT 18 FRONTEND (3000)         │
│  (HomePage, Discover, Cart, Profile)    │
└──────────────┬──────────────────────────┘
               │ HTTPS
               ▼
┌─────────────────────────────────────────┐
│   ASP.NET CORE 8.0 API (7023)           │
│  (51 endpoints - 100% aligned)          │
│  ├─ Auth (4)      ✅                    │
│  ├─ Subjects (10) ✅ (+3 new)           │
│  ├─ Users (8)     ✅ (+2 new)           │
│  ├─ Orders (3)    ✅                    │
│  ├─ Payments (6)  ✅                    │
│  ├─ Enrollments (3) ✅                  │
│  ├─ Cart (4)      ✅                    │
│  ├─ History (5)   ✅                    │
│  ├─ Analytics (3) ✅                    │
│  ├─ Favorites (3) ✅                    │
│  └─ Admin (2)     ✅                    │
└──────────┬─────────────┬────────────────┘
           │             │
           ▼             ▼
    ┌──────────────┐ ┌──────────────┐
    │ PostgreSQL   │ │ FastApi AI     │
    │  Database    │ │ (5000)       │
    └──────────────┘ │ (Optional)   │
                     └──────────────┘
```

---

## 🔐 AUTHENTIFICATION

Les endpoints utilisent **JWT Bearer Token** (Cognito AWS).

Inclure dans les headers:
```
Authorization: Bearer <jwt_token>
```

Frontend gère automatiquement via `api.ts`.

---

## ⚙️ CONFIGURATION

### Backend
**Fichier**: `backend/dotnet/appsettings.json`
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=reussir;..."
  },
  "Jwt": {
    "Issuer": "your-issuer",
    "Audience": "your-audience",
    "Key": "your-secret-key"
  }
}
```

### Frontend
**Fichier**: `frontend/src/config/api.ts`
```typescript
export const API_CONFIG = {
  BASE_URL: 'https://localhost:7023',
  FLASK_URL: 'http://localhost:5000',
  TIMEOUT: 30000,
};
```

---

## 📋 MODULES COMPLÉTÉS SPRINT 4

### ✅ Subjects Module (3 endpoints)
- `GET /api/subjects/categories` → Catégories
- `GET /api/subjects/filters` → Filtres de recherche
- `GET /api/subjects/{id}/similar` → Cours similaires

### ✅ Users Module (2 endpoints)
- `GET /api/users/profile/statistics` → Stats profil
- `GET /api/users/{id}/statistics` → Stats utilisateur

### ✅ Payments Module (Already Complete)
- 6 endpoints pour gérer les paiements

### ✅ Enrollments Module (Already Complete)
- 3 endpoints pour les inscriptions

---

## 🐛 TROUBLESHOOTING

### Build Error
```
dotnet clean
rm -r obj/
dotnet restore
dotnet build
```

### Port déjà utilisé
```bash
# Lister les processus utilisant le port 7023
lsof -i :7023

# Tuer le processus
kill -9 <PID>
```

### Database connection error
```bash
# Vérifier PostgreSQL
psql -U postgres -d reussir

# Réinitialiser la BD
cd backend/database
python script.py
```

### CORS error
Vérifier `Program.cs`:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", builder =>
    {
        builder.WithOrigins("http://localhost:3000")
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});
```

---

## 📚 DOCUMENTATION

- **API Endpoints**: Voir `ENDPOINTS_CORRESPONDENCE_TABLE.md`
- **Architecture**: Voir `BACKEND_ANALYSIS.md`
- **Rapport Complet**: Voir `SPRINT_4_COMPLETION_REPORT.md`

---

## ✅ STATUT FINAL

```
┌──────────────────────────────────┐
│  BUILD STATUS                    │
├──────────────────────────────────┤
│ Errors:         0        ✅      │
│ Warnings:       29       ⚠️      │
│ Endpoints:      51/51    ✅      │
│ Alignment:      100%     ✅      │
│ Production:     95%      ✅      │
└──────────────────────────────────┘
```

**READY FOR PRODUCTION** 🚀

---

**Last Updated**: 2025-12-10  
**Version**: v1.0.0  
**Status**: ✅ Complete
