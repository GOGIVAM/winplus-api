# Database Migration Execution Guide

## 📋 Étapes d'Exécution des Migrations

### Étape 1: Créer la Migration EF Core
```bash
cd backend/dotnet
dotnet ef migrations add CustomAuthMigration --context ApplicationDbContext --output-dir Migrations
```

### Étape 2: Appliquer la Migration à la Base de Données
```bash
# Development (localhost)
dotnet ef database update --context ApplicationDbContext

# Production (EC2)
# Option 1: Ligne de commande
dotnet ef database update --context ApplicationDbContext \
    --connection "Host=172.31.20.230;Port=5432;Database=winplus_db;Username=gogivam;Password=YOUR_PASSWORD;SslMode=Require;"

# Option 2: SQLDirect (via DATABASE_MIGRATION_SCRIPTS.sql)
psql -h 172.31.20.230 -U gogivam -d winplus_db -f DATABASE_MIGRATION_SCRIPTS.sql
```

### Étape 3: Vérifier la Création des Tables
```sql
-- Via psql
psql -h 172.31.20.230 -U gogivam -d winplus_db

-- Depuis la ligne de commande psql:
\dt
-- Chercher: RefreshTokens, DeviceInfos, EmailVerificationTokens, etc.

-- Ou SQL direct:
SELECT table_name FROM information_schema.tables 
WHERE table_schema = 'public' 
AND table_name IN ('RefreshTokens', 'DeviceInfos', 'EmailVerificationTokens');
```

## 🔄 Migration Rollback (si erreur)

```bash
# Supprimer la dernière migration (si pas appliquée)
dotnet ef migrations remove --context ApplicationDbContext

# Revenir à une migration antérieure
dotnet ef database update [NomMigrationPrecedente] --context ApplicationDbContext
```

## 📊 Tables Créées et Structure

| Table | Colonnes Clés | Foreign Key |
|-------|---------------|-------------|
| **RefreshTokens** | Id, UserId, Token, ExpiresAt, RevokedAt | Users(Id) |
| **DeviceInfos** | Id, UserId, DeviceFingerprint, RememberUntil | Users(Id) |
| **EmailVerificationTokens** | Id, UserId, VerificationCode, ExpiresAt | Users(Id) |
| **PasswordResetTokens** | Id, UserId, Token, ExpiresAt, IsUsed | Users(Id) |
| **TwoFactorTokens** | Id, UserId, TotpSecret, IsTotpEnabled | Users(Id) 1:1 |
| **BackupCodes** | Id, TwoFactorTokenId, Code, IsUsed | TwoFactorTokens(Id) |
| **OAuthAccounts** | Id, UserId, Provider, ProviderUserId | Users(Id) |

**Users Table Updates:**
- ✅ EmailVerified (BOOLEAN)
- ✅ PasswordHash (VARCHAR)
- ✅ LastLoginAt (TIMESTAMP)

---

**⚠️ IMPORTANT:** Exécuter les migrations AVANT de déployer le backend!
