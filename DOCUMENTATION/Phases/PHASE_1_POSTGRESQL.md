# 🚀 PLAN D'ACTION - PHASE 1: POSTGRESQL SETUP

**Status**: 🔴 À DÉMARRER  
**Durée Estimée**: 30-45 minutes  
**Objectif**: Base PostgreSQL fonctionnelle avec EF Core

---

## 📋 CHECKLIST PHASE 1

### 1. **Installation des Packages NuGet** (5 min)
```
[ ] Npgsql.EntityFrameworkCore.PostgreSQL
[ ] Microsoft.EntityFrameworkCore.Tools
[ ] Microsoft.EntityFrameworkCore.Design
```

### 2. **Création du DbContext** (10 min)
```
[ ] Créer Data/ApplicationDbContext.cs
[ ] Configurer OnModelCreating
[ ] Ajouter tous les DbSets
```

### 3. **Modèles Entity** (10 min)
```
[ ] Models/User.cs
[ ] Models/Subject.cs
[ ] Models/CourseContent.cs
[ ] Models/Enrollment.cs
[ ] Models/CartItem.cs
[ ] Models/Order.cs
[ ] Models/OrderItem.cs
[ ] Models/Favorite.cs
[ ] Models/LearningHistory.cs
[ ] Models/Notification.cs
```

### 4. **Configuration appsettings.json** (5 min)
```
[ ] Ajouter ConnectionString PostgreSQL
[ ] Configurer logging
```

### 5. **Program.cs - Enregistrer les Services** (5 min)
```
[ ] builder.Services.AddDbContext
[ ] Configurer Entity Framework
```

### 6. **Migrations EF Core** (5 min)
```
[ ] dotnet ef migrations add InitialCreate
[ ] dotnet ef database update
```

### 7. **Vérification** (5 min)
```
[ ] Tables créées dans PostgreSQL
[ ] Swagger documentation mise à jour
[ ] Build sans erreurs
```

---

## 🎯 ORDRE D'EXÉCUTION

### Étape 1: Installer les Packages
```bash
cd backend/dotnet
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Tools
dotnet add package Microsoft.EntityFrameworkCore.Design
```

### Étape 2: Créer les Modèles Entity
À créer:
- `Models/Entities/User.cs`
- `Models/Entities/Subject.cs`
- `Models/Entities/CourseContent.cs`
- `Models/Entities/Enrollment.cs`
- `Models/Entities/CartItem.cs`
- `Models/Entities/Order.cs`
- `Models/Entities/OrderItem.cs`
- `Models/Entities/Favorite.cs`
- `Models/Entities/LearningHistory.cs`
- `Models/Entities/Notification.cs`

### Étape 3: Créer le DbContext
À créer:
- `Data/ApplicationDbContext.cs`

### Étape 4: Configurer appsettings
À mettre à jour:
- `appsettings.Development.json`
- `appsettings.json`

### Étape 5: Enregistrer dans Program.cs
À mettre à jour:
- `Program.cs` - AddDbContext

### Étape 6: Exécuter les Migrations
```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

---

## 📝 COMMANDES GIT

```bash
# Après chaque section
git add .
git commit -m "feat: [description de la modification]"

# Ex:
git commit -m "feat: Add PostgreSQL NuGet packages and Entity Framework setup"
git commit -m "feat: Create Entity models for database schema"
git commit -m "feat: Create ApplicationDbContext with all DbSets"
git commit -m "feat: Configure PostgreSQL connection string"
git commit -m "feat: Register DbContext in Program.cs"
git commit -m "feat: Execute EF migrations and create database"
```

---

## 🔗 DÉPENDANCES ENTRE ÉTAPES

```
1. Installer Packages
    ↓
2. Créer Modèles Entity
    ↓
3. Créer DbContext
    ↓
4. Configurer appsettings
    ↓
5. Enregistrer services dans Program.cs
    ↓
6. Exécuter migrations
    ↓
7. Vérifier la base de données
```

---

## ⚡ POINTS IMPORTANTS

1. **PostgreSQL doit être installé et running**
   - Windows: PostgreSQL 14+ requis
   - Service: `pg_ctl -D "C:\Program Files\PostgreSQL\14\data" start`
   - Ou: pgAdmin UI

2. **Fichier .env pour les secrets**
   ```
   DB_PASSWORD=your_secure_password
   DB_HOST=localhost
   DB_PORT=5432
   DB_NAME=reussir_db
   ```

3. **Validation EF Core**
   ```bash
   dotnet ef dbcontext info
   dotnet ef migrations list
   ```

4. **Seed Data (optionnel pour Phase 1)**
   - À faire en Phase 2

5. **Convention de nommage:**
   - Classes: PascalCase
   - Propriétés: PascalCase
   - Tables: snake_case (EF convertit automatiquement)

---

## 💡 RESSOURCES

- [EF Core PostgreSQL](https://www.npgsql.org/efcore/)
- [EF Core Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core Relationships](https://learn.microsoft.com/en-us/ef/core/modeling/relationships)

---

**Prêt à commencer la Phase 1 ? ✅**

Dis-moi "Commencer Phase 1" et je vais:
1. Installer les packages
2. Créer les modèles Entity
3. Créer le DbContext
4. Configurer appsettings
5. Enregistrer les services
6. Exécuter les migrations
