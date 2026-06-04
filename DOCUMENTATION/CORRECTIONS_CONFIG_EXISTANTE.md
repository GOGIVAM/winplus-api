# 🔄 CORRECTIONS AUDIT - Configuration Existante Winplus

**Date:** 20 janvier 2026  
**Statut:** Configuration backend DÉJÀ EN PLACE - Mise à jour audit

---

## 📋 ANALYSE CONFIGURATION EXISTANTE

### ✅ DÉCOUVERTE IMPORTANTE

Vous disposez **déjà** de 3 fichiers `appsettings.json` correctement configurés :
- `backend\dotnet\appsettings.json` (Production)
- `backend\dotnet\appsettings.Development.json` (Développement)
- `backend\dotnet\appsettings.Production.json` (Production EC2)

**Impact sur l'audit :** Les corrections proposées dans l'Objectif 2 pour la création des fichiers `appsettings.json` sont **PARTIELLEMENT OBSOLÈTES**. Cependant, certaines améliorations restent nécessaires.

---

## 📊 COMPARAISON CONFIGURATION

### Configuration Production (`appsettings.Production.json`)

#### ✅ **CE QUI EST BIEN**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=172.31.20.230;Port=5432;Database=winplus_db;Username=gogivam;Password=Admin001;SslMode=Require;"
  },
  "AWS": {
    "Region": "us-east-1",
    "UserPoolId": "us-east-1_3vDfozXgb",
    "UserPoolClientId": "3gcav7h9ruq9duuf7bv44ll1a8",
    "UseCognito": true
  },
  "AIService": {
    "BaseUrl": "http://localhost:5000",
    "TimeoutSeconds": 30,
    "RetryAttempts": 3
  },
  "Cors": {
    "AllowedOrigins": [
      "https://winplus.com",
      "https://www.winplus.com",
      "https://winplus-staging.com"
    ]
  }
}
```

✅ **Points forts :**
- PostgreSQL sur IP privée EC2 (172.31.20.230)
- SSL activé pour la DB
- AWS Cognito correctement configuré
- CORS configuré pour production
- Retry/Timeout pour AI Service

#### ⚠️ **PROBLÈMES IDENTIFIÉS**

##### 🔴 **CRITIQUE 1 : FastApi URL en localhost en Production**
```json
"AIService": {
  "BaseUrl": "http://localhost:5000",  // ❌ Ne fonctionnera pas en prod
}
```

**Problème :** En production, FastApi devrait être sur un service distant ou un endpoint EC2 séparé, pas localhost.

**Solution :**
```json
"AIService": {
  "BaseUrl": "http://172.31.20.230:5000",  // ✅ IP privée EC2 du serveur FastApi
  // OU
  "BaseUrl": "https://winplus.com/fastapi",  // ✅ Reverse proxy Nginx
}
```

##### 🟡 **IMPORTANT 1 : Section JWT Manquante**

Votre config Production n'a **pas de section `Jwt`** alors qu'elle existe dans Development. AWS Cognito ne nécessite pas de `SecretKey` (il utilise les clés publiques JWKS), mais vous devez avoir la configuration pour la validation :

```json
"JWT": {
  "Issuer": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_3vDfozXgb",
  "Audience": "3gcav7h9ruq9duuf7bv44ll1a8",
  "ValidateLifetime": true,
  "ClockSkew": "00:05:00"
}
```

##### 🟡 **IMPORTANT 2 : FastApiApiUrl Dupliqué**

Vous avez deux clés pour la même chose :
```json
"AIService": { "BaseUrl": "..." },
"FastApiApiUrl": "http://localhost:5000"  // ❌ Duplication
```

**Recommandation :** Supprimer `FastApiApiUrl` et utiliser uniquement `AIService:BaseUrl`.

---

### Configuration Development (`appsettings.Development.json`)

#### ✅ **CE QUI EST BIEN**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=winplus_db;Username=postgres;Password='Mkomegmbdysdia4';SslMode=Disable;Include Error Detail=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",  // ✅ Bon pour debug
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:4200",
      "http://localhost:8080",
      "http://localhost:5173"  // ✅ Vite dev server
    ]
  }
}
```

✅ **Points forts :**
- Logs détaillés pour développement
- CORS permissif pour tous les ports locaux courants
- Include Error Detail activé

#### ⚠️ **PROBLÈMES IDENTIFIÉS**

##### 🟡 **IMPORTANT : Section JWT Manquante Aussi**

Même problème qu'en Production - pas de section `JWT` pour Cognito.

##### 🟢 **SOUHAITABLE : Password en Clair**

Bien que ce soit Development, il serait mieux d'utiliser des secrets :
```bash
# User Secrets pour Development
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;..."
```

---

### Configuration appsettings.json (Base)

#### 🔴 **CRITIQUE : JWT SecretKey Vide**

```json
"Jwt": {
  "SecretKey": "",  // ❌ VIDE = FAILLE DE SÉCURITÉ
  "Issuer": "winplusApp",
  "Audience": "winplusUsers",
  "ExpirationMinutes": 60
}
```

**Problème :** Si vous utilisez JWT local (non-Cognito), cette clé vide compromet toute la sécurité.

**Solutions :**

**Option A : Vous utilisez UNIQUEMENT Cognito (recommandé)**
```json
// Supprimer la section Jwt locale
// Ajouter uniquement la config JWT pour Cognito
"JWT": {
  "Issuer": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_3vDfozXgb",
  "Audience": "3gcav7h9ruq9duuf7bv44ll1a8",
  "ValidateLifetime": true,
  "ClockSkew": "00:05:00"
}
```

**Option B : Vous voulez JWT local + Cognito**
```json
"Jwt": {
  "SecretKey": "your-generated-secret-key-minimum-32-characters-use-guid-or-crypto",
  "Issuer": "winplusApp",
  "Audience": "winplusUsers",
  "ExpirationMinutes": 2880  // 48h
}
```

Générer une clé sécurisée :
```bash
# PowerShell
[Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([System.Guid]::NewGuid().ToString() + [System.Guid]::NewGuid().ToString()))

# Linux/Mac
openssl rand -base64 64
```

#### ⚠️ **ATTENTION : Kestrel Endpoints**

```json
"Kestrel": {
  "Endpoints": {
    "Http": {
      "Url": "http://localhost:5047"
    },
    "Https": {
      "Url": "https://localhost:7023"
    }
  }
}
```

⚠️ **Vérification nécessaire :** Ces ports correspondent-ils à votre `VITE_API_URL` frontend ?

**Frontend (_env) :**
```bash
VITE_API_URL=http://localhost:5001  # ❌ PORT DIFFÉRENT !
```

**Solution :** Aligner les ports :
- **Option 1 :** Changer Kestrel en 5001 (standard ASP.NET)
- **Option 2 :** Changer VITE_API_URL en 5047

---

## 🔧 CORRECTIONS NÉCESSAIRES

### ✅ CORRECTION 1 : Fichier Production Complet

**Fichier :** `backend\dotnet\appsettings.Production.json`

**Remplacer ENTIÈREMENT par :**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=172.31.20.230;Port=5432;Database=winplus_db;Username=gogivam;Password=Admin001;SslMode=Require;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "backend": "Information"
    }
  },
  "AllowedHosts": "*",
  
  "AIService": {
    "BaseUrl": "http://172.31.20.230:5000",
    "TimeoutSeconds": 60,
    "RetryAttempts": 3,
    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerBreakDuration": "00:00:30"
  },
  
  "AWS": {
    "Region": "us-east-1",
    "UserPoolId": "us-east-1_3vDfozXgb",
    "UserPoolClientId": "3gcav7h9ruq9duuf7bv44ll1a8",
    "UseCognito": true
  },
  
  "JWT": {
    "Issuer": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_3vDfozXgb",
    "Audience": "3gcav7h9ruq9duuf7bv44ll1a8",
    "ValidateLifetime": true,
    "ClockSkew": "00:05:00"
  },
  
  "Cors": {
    "AllowedOrigins": [
      "https://winplus.com",
      "https://www.winplus.com",
      "https://winplus-staging.com",
      "https://gogivamback.com"
    ]
  },
  
  "Swagger": {
    "Enabled": false,
    "Title": "WinPlus Educational AI Gateway API",
    "Version": "v3.0",
    "Description": "API Gateway pour WinPlus - Intégration des services d'IA éducatifs",
    "ContactName": "Support Technique",
    "ContactEmail": "support@gogivam.com"
  }
}
```

**Changements appliqués :**
1. ✅ `AIService.BaseUrl` pointant vers IP privée EC2 FastApi (pas localhost)
2. ✅ Timeout augmenté à 60s pour requêtes IA
3. ✅ Circuit Breaker activé
4. ✅ Section `JWT` ajoutée pour validation Cognito
5. ✅ `FastApiApiUrl` supprimé (duplication)
6. ✅ `AITimeoutSeconds` et `AICacheExpireMins` supprimés (gérés par AIService)
7. ✅ Swagger désactivé en production

---

### ✅ CORRECTION 2 : Fichier Development Complet

**Fichier :** `backend\dotnet\appsettings.Development.json`

**Remplacer ENTIÈREMENT par :**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=winplus_db;Username=postgres;Password=Mkomegmbdysdia4;SslMode=Disable;Include Error Detail=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information",
      "backend": "Debug"
    }
  },
  "AllowedHosts": "*",
  
  "AIService": {
    "BaseUrl": "http://localhost:5000",
    "TimeoutSeconds": 60,
    "RetryAttempts": 3,
    "EnableCircuitBreaker": false
  },
  
  "AWS": {
    "Region": "us-east-1",
    "UserPoolId": "us-east-1_3vDfozXgb",
    "UserPoolClientId": "3gcav7h9ruq9duuf7bv44ll1a8",
    "UseCognito": true
  },
  
  "JWT": {
    "Issuer": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_3vDfozXgb",
    "Audience": "3gcav7h9ruq9duuf7bv44ll1a8",
    "ValidateLifetime": true,
    "ClockSkew": "00:05:00"
  },
  
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:4200",
      "http://localhost:5173",
      "http://localhost:8080"
    ]
  },
  
  "Swagger": {
    "Enabled": true,
    "Title": "Educational AI Gateway API - Development",
    "Version": "v3.0",
    "Description": "API Gateway pour le service IA éducative - Recommandations, Analyse, Quizzes, Analytics",
    "ContactName": "Support Technique",
    "ContactEmail": "support@winplus.web.com"
  },
  
  "DetailedErrors": true
}
```

**Changements appliqués :**
1. ✅ Section `JWT` ajoutée
2. ✅ Logs EF Core SQL activés
3. ✅ Circuit Breaker désactivé (dev)
4. ✅ `FastApiApiUrl` supprimé
5. ✅ Swagger explicitement activé

---

### ✅ CORRECTION 3 : Fichier Base (appsettings.json)

**Fichier :** `backend\dotnet\appsettings.json`

**Option A : Si vous utilisez UNIQUEMENT Cognito (RECOMMANDÉ)**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=winplus_db;Username=miguel;Password=Mkomegmbdysdia4;Include Error Detail=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  "AWS": {
    "Region": "us-east-1",
    "UserPoolId": "us-east-1_3vDfozXgb",
    "UserPoolClientId": "3gcav7h9ruq9duuf7bv44ll1a8",
    "UseCognito": true
  },
  
  "JWT": {
    "Issuer": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_3vDfozXgb",
    "Audience": "3gcav7h9ruq9duuf7bv44ll1a8",
    "ValidateLifetime": true,
    "ClockSkew": "00:05:00"
  },
  
  "AIService": {
    "BaseUrl": "http://localhost:5000",
    "TimeoutSeconds": 60,
    "RetryAttempts": 3,
    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerBreakDuration": "00:00:30"
  },
  
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5001"
      },
      "Https": {
        "Url": "https://localhost:7023"
      }
    }
  },
  
  "Smtp": {
    "Server": "smtp.gmail.com",
    "Port": 587,
    "Username": "georgesmoukoko4@gmail.com",
    "Password": "jujr lbux girk hklo",
    "EnableSsl": true,
    "FromEmail": "georgesmoukoko4@gmail.com",
    "FromName": "Winplus Support"
  },
  
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:5173",
      "https://winplus.com",
      "https://gogivamback.com"
    ]
  },
  
  "Swagger": {
    "Enabled": true,
    "Title": "WinPlus Educational API",
    "Version": "v3.0",
    "Description": "API principale pour WinPlus",
    "ContactName": "Support Technique",
    "ContactEmail": "support@winplus.com"
  }
}
```

**Changements appliqués :**
1. ✅ Section `Jwt` locale **SUPPRIMÉE** (SecretKey vide dangereux)
2. ✅ Section `JWT` Cognito **AJOUTÉE**
3. ✅ Kestrel port HTTP changé de 5047 → **5001** (standard, aligné avec frontend)
4. ✅ Section AIService complète avec Circuit Breaker
5. ✅ Section CORS ajoutée

---

**Option B : Si vous voulez JWT Local + Cognito**

Ajouter cette section en PLUS de la section JWT Cognito :

```json
"LocalJwt": {
  "SecretKey": "VotreCléGénéréeParCryptoOuGuidMinimum32Caractères",
  "Issuer": "WinPlusApp",
  "Audience": "WinPlusUsers",
  "ExpirationMinutes": 2880
}
```

Et générer une vraie clé :
```powershell
# PowerShell
$guid1 = [System.Guid]::NewGuid().ToString("N")
$guid2 = [System.Guid]::NewGuid().ToString("N")
$combined = $guid1 + $guid2
Write-Host "Votre clé : $combined"
```

---

## 🔄 MISE À JOUR FastApiClient.cs

Votre FastApiClient doit lire depuis `AIService:BaseUrl` au lieu de `FastApiApiUrl`.

**Vérifier dans FastApiClient.cs (ligne ~36) :**

```csharp
// ✅ CORRECT
_baseUrl = _configuration["AIService:BaseUrl"] ?? "http://localhost:5000";

// ❌ À ÉVITER (ancienne version)
_baseUrl = _configuration["FastApiApiUrl"] ?? "http://localhost:5000";
```

Si votre code utilise encore `FastApiApiUrl`, utilisez la correction fournie dans l'audit Objectif 2 (FastApiClient.cs complet).

---

## 🔄 MISE À JOUR Program.cs

Assurez-vous que votre `Program.cs` charge correctement la configuration JWT :

```csharp
// Configuration JWT (Cognito)
var jwtSettings = builder.Configuration.GetSection("JWT");
var awsSettings = builder.Configuration.GetSection("AWS");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtSettings["Issuer"]; // Cognito authority
        options.Audience = jwtSettings["Audience"];
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateLifetime = jwtSettings.GetValue<bool>("ValidateLifetime", true),
            ClockSkew = TimeSpan.Parse(jwtSettings["ClockSkew"] ?? "00:05:00")
        };
    });
```

---

## 📋 VÉRIFICATIONS À EFFECTUER

### ✅ Checklist Immédiate

```bash
# 1. Vérifier la connexion PostgreSQL en Development
cd backend/dotnet
dotnet ef database update

# 2. Vérifier que l'API démarre sur le bon port
dotnet run
# Devrait afficher: "Now listening on: http://localhost:5001"

# 3. Tester la connexion FastApi
curl http://localhost:5000/health

# 4. Tester health check backend
curl http://localhost:5001/api/health

# 5. Vérifier JWT Cognito
curl http://localhost:5001/api/cart \
  -H "Authorization: Bearer VOTRE_TOKEN_COGNITO"
# Devrait retourner 401 si pas de token
# Devrait retourner 200 avec un token valide
```

---

## 🔀 ALIGNEMENT PORTS FRONTEND ↔ BACKEND

### Situation Actuelle

**Backend (appsettings.json) :**
```json
"Kestrel": {
  "Endpoints": {
    "Http": { "Url": "http://localhost:5047" }  // ❌ Port 5047
  }
}
```

**Frontend (_env) :**
```bash
VITE_API_URL=http://localhost:5001  # ❌ Port 5001
```

### ✅ Solution Recommandée

**Option 1 : Standardiser sur 5001 (RECOMMANDÉ)**

Modifier `appsettings.json` :
```json
"Kestrel": {
  "Endpoints": {
    "Http": { "Url": "http://localhost:5001" }  // ✅ Standard ASP.NET
  }
}
```

**Option 2 : Modifier Frontend**

Modifier `_env` :
```bash
VITE_API_URL=http://localhost:5047  # ✅ Aligné avec Kestrel actuel
```

---

## 📊 RÉSUMÉ DES CORRECTIONS

| Fichier | Correction | Priorité | Statut |
|---------|------------|----------|--------|
| `appsettings.Production.json` | FastApi URL + JWT section | 🔴 Critique | À faire |
| `appsettings.Development.json` | JWT section | 🟡 Important | À faire |
| `appsettings.json` | JWT SecretKey + Port | 🔴 Critique | À faire |
| `FastApiClient.cs` | Lire AIService:BaseUrl | 🟡 Important | Vérifier |
| `Program.cs` | JWT config Cognito | 🟡 Important | Vérifier |
| `_env` frontend | Port aligné | 🟡 Important | À faire |

---

## 🎯 PLAN D'ACTION IMMÉDIAT

### Étape 1 : Backup (5 min)
```bash
cd backend/dotnet
cp appsettings.json appsettings.json.backup
cp appsettings.Development.json appsettings.Development.json.backup
cp appsettings.Production.json appsettings.Production.json.backup
```

### Étape 2 : Appliquer Corrections (15 min)
1. Remplacer les 3 fichiers appsettings par les versions corrigées ci-dessus
2. Vérifier FastApiClient.cs utilise `AIService:BaseUrl`
3. Aligner port frontend avec backend (5001 ou 5047)

### Étape 3 : Tests (10 min)
```bash
# Test compilation
dotnet build

# Test démarrage
dotnet run

# Test endpoints
curl http://localhost:5001/api/health
```

### Étape 4 : Déploiement EC2 (30 min)
1. Copier `appsettings.Production.json` sur EC2
2. Vérifier FastApi tourne sur `172.31.20.230:5000`
3. Redémarrer service backend EC2
4. Tester health check depuis EC2

---

## ⚠️ NOTES IMPORTANTES

1. **Password SMTP en Clair :** Votre password Gmail est visible. Considérez utiliser des secrets :
   ```bash
   dotnet user-secrets set "Smtp:Password" "jujr lbux girk hklo"
   ```

2. **Password DB en Clair :** Même remarque pour les passwords PostgreSQL. En production, utilisez AWS Secrets Manager ou Parameter Store.

3. **FastApi en Production :** Assurez-vous que FastApi tourne bien sur `172.31.20.230:5000` (IP privée EC2) et non localhost.

4. **SSL PostgreSQL :** Production utilise `SslMode=Require` mais avec un password en clair dans le fichier. Utilisez AWS RDS avec IAM authentication pour plus de sécurité.

---

## 📝 CONCLUSION

Votre configuration existante est **globalement bonne** mais nécessite ces corrections critiques :

🔴 **CRITIQUE :**
- FastApi URL en production (localhost → IP EC2)
- JWT SecretKey vide (sécurité)
- Section JWT manquante (Cognito)
- Port frontend/backend désalignés

🟡 **IMPORTANT :**
- Duplication FastApiApiUrl
- Secrets en clair dans fichiers

🟢 **SOUHAITABLE :**
- User Secrets pour Development
- AWS Secrets Manager pour Production

Appliquez ces corrections et votre système sera **opérationnel et sécurisé** ! 🚀
