# 🔍 AUDIT BACKEND - Namespaces & Signatures

## 📋 Résumé
- **Total fichiers**: ~60 fichiers .cs
- **Fichiers problématiques**: À déterminer
- **État**: EN COURS D'AUDIT

---

## 🚨 PROBLÈMES IDENTIFIÉS

### 1. **Program.cs**
- ❌ Missing: `using Backend.Services;` for AIServiceWithDatabase registration
- ❌ Enregistrement AIServiceWithDatabase avec namespace complet (double namespace)

### 2. **Controllers/AIController.cs**
- ✅ Namespace: `Backend.Controllers` (correct)
- ✅ Using statements: `Backend.Models.DTOs`, `Backend.Services` (correct)
- ⚠️ Status: À vérifier qu'il appelle bien AIService

### 3. **Controllers/AdminController.cs**
- ✅ Namespace: `Backend.Controllers` (correct avec `;` file-scoped)
- ✅ Using statements: `Backend.Models.DTOs`, `Backend.Services` (correct)

### 4. **Controllers/AuthController.cs**
- ❌ Using: `EducationalAI.Services` (MAUVAIS - devrait être `Backend.Services`)
- ❌ Using: `EducationalAI.Models` (MAUVAIS - devrait être `Backend.Models`)
- ❌ Namespace: Non visible dans audit - à vérifier

### 5. **Services/AIService.cs**
- ❌ Namespace: `backend.Services` (minuscule - devrait être `Backend.Services`)
- ❌ Missing: DTOs namespace correction

### 6. **Services/FastApiClient.cs**
- ❌ Namespace: `backend.Services` (minuscule - devrait être `Backend.Services`)
- ❌ Missing: DTOs namespace correction

### 7. **Services/AIServiceWithDatabase.cs**
- ⚠️ À SUPPRIMER (complexité inutile - on utilise FastApi)

### 8. **Models/DTOs/AIDTO.cs**
- ✅ Namespace: `Backend.Models.DTOs` (correct)

### 9. **Data/ApplicationDbContext.cs**
- ✅ Namespace: `Backend.Data` (correct avec `;` file-scoped)
- ✅ Using: `Backend.Models.Entities` (correct)

---

## 🎯 FICHIERS À CORRIGER (Priorité)

### **CRITIQUE** (bloquent le build)
1. [ ] `Services/AIService.cs` - Namespace minuscule
2. [ ] `Services/FastApiClient.cs` - Namespace minuscule
3. [ ] `Services/AIServiceWithDatabase.cs` - À SUPPRIMER
4. [ ] `Controllers/AuthController.cs` - Mauvais namespace (EducationalAI)
5. [ ] `Program.cs` - Enregistrement AIService problématique

### **IMPORTANT** (À vérifier)
6. [ ] Tous les Services/*.cs - Vérifier namespace `Backend.Services`
7. [ ] Tous les Controllers/*.cs - Vérifier namespace `Backend.Controllers`
8. [ ] Tous les Models/DTOs/*.cs - Vérifier namespace `Backend.Models.DTOs`
9. [ ] Tous les Repositories/*.cs - Vérifier namespace `Backend.Repositories`
10. [ ] Tous les Models/Entities/*.cs - Vérifier namespace `Backend.Models.Entities`

---

## ✅ ACTION PLAN

```
Phase 1: Nettoyer les Services (5 min)
  ├─ Supprimer AIServiceWithDatabase.cs
  ├─ Corriger AIService.cs namespace
  └─ Corriger FastApiClient.cs namespace

Phase 2: Corriger Controllers (5 min)
  ├─ Corriger AuthController.cs namespaces (EducationalAI → Backend)
  └─ Vérifier tous les autres Controllers

Phase 3: Vérifier Models & DTOs (5 min)
  └─ Audit complet AIDTO.cs, AdminDTOs.cs, etc

Phase 4: Corriger Program.cs (3 min)
  ├─ Retirer enregistrement AIServiceWithDatabase
  ├─ Garder AIService qui appelle FastApi
  └─ Rebuild

Phase 5: Build & Test (5 min)
  ├─ dotnet build
  ├─ dotnet test
  └─ FastApi health check
```

---

## 📝 NOTES

- **Règle**: Tous les namespaces doivent être `Backend.*` (Capital B)
- **Exception**: `EducationalAI.*` est un ancien namespace à remplacer partout
- **FastApi**: Garder seulement l'appel FastApi dans AIService
- **DTOs**: Doivent être dans `Backend.Models.DTOs`
