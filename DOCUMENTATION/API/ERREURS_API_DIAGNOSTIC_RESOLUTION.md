# 🔧 Diagnostic et Résolution des Erreurs API (500 & 400)

Date: 3 février 2026

## 📋 Résumé des Erreurs

### ❌ Erreur 1: GET `/api/cart` → 500 Internal Server Error
```
GET http://44.200.166.163/api/cart 500 (Internal Server Error)
[API Error] {url: '/cart', method: 'get', status: 500, message: 'Erreur 500', data: 'Erreur serveur'}
```

### ❌ Erreur 2: POST `/api/auth/signup` → 400 Bad Request
```
POST http://44.200.166.163/api/auth/signup 400 (Bad Request)
[API Error] {url: '/auth/signup', method: 'post', status: 400, message: "Erreur lors de l'inscription"}
```

---

## 🔍 Causes Identifiées

### **Problème 1: Token JWT manquant ou invalide (Erreur 500 - Cart)**

#### Cause racine:
- La requête GET `/cart` **ne transmet pas le token d'authentification**
- Le backend tente d'extraire `userId` du token via `User.GetUserId()`
- Sans token valide → Exception `UnauthorizedAccessException` → 500

#### Points critiques:
1. **Frontend** (`api.ts`):
   - ✅ Ajoute le header `Authorization: Bearer ${token}` si le token existe
   - ❌ Mais le token peut être `null` ou `undefined` dans `localStorage`

2. **Backend** (`CartController.cs`):
   - ❌ Le catch bloc retourne un **500** même pour les erreurs d'authentification
   - ❌ Pas de vérification de `User?.Identity?.IsAuthenticated`

#### Solutions appliquées ✅:
```csharp
// AVANT (problématique):
var userId = User.GetUserId();  // Lance une exception si le token est invalide

// APRÈS (corrigé):
if (User?.Identity?.IsAuthenticated != true)
    return Unauthorized(new { error = "Authentication required" });

try {
    userId = User.GetUserId();
} catch (UnauthorizedAccessException ex) {
    _logger.LogWarning(ex, "Failed to extract userId from token claims");
    return Unauthorized(new { error = "Invalid token", details = ex.Message });
}
```

---

### **Problème 2: SignUpRequestDto mal validé (Erreur 400 - Signup)**

#### Cause racine:
- Le frontend envoie `confirmPassword` mais le **DTO backend** n'a pas ce champ
- Les champs `firstName` et `lastName` sont **optionnels** dans le DTO
- Si Cognito reçoit des valeurs `null/empty`, il peut rejeter l'inscription

#### Points critiques:
1. **Frontend** (`auth.ts`):
   ```typescript
   const response = await api.post<any>('/auth/signup', {
     firstName: data.firstName,      // ← Peut être undefined
     lastName: data.lastName,        // ← Peut être undefined
     email: data.email,
     password: data.password,
     confirmPassword: data.confirmPassword,  // ← Non utilisé par le backend
   });
   ```

2. **Backend** (`AuthController.cs`):
   ```csharp
   // AVANT:
   request.FirstName ?? ""    // Accepte une string vide
   request.LastName ?? ""     // Accepte une string vide
   
   // PROBLÈME: Cognito n'aime pas les strings vides pour les noms
   ```

#### Solutions appliquées ✅:
```csharp
// Validation améliorée
if (string.IsNullOrWhiteSpace(request.Password))
    return BadRequest(new { error = "Password is required" });

if (request.Password.Length < 8)
    return BadRequest(new { error = "Password must be at least 8 characters" });

// Fallback intelligent pour firstName/lastName
var firstName = request.FirstName?.Trim() ?? 
                request.Name?.Split(' ').FirstOrDefault() ?? "";
var lastName = request.LastName?.Trim() ?? 
               (request.Name?.Contains(' ') == true ? 
                   string.Join(" ", request.Name.Split(' ').Skip(1)) : "");

// Logging détaillé
_logger.LogInformation(
    "SignUp attempt for email: {Email}, firstName: {FirstName}, lastName: {LastName}", 
    request.Email, firstName, lastName);
```

---

## 🛠️ Actions de Correction

### ✅ Correction 1: CartController.cs
**Fichier**: `m:\win\winplus\backend\dotnet\Controllers\CartController.cs`

**Changements**:
1. Ajout de vérification d'authentification
2. Meilleure gestion des erreurs de token
3. Logging détaillé des erreurs réelles

### ✅ Correction 2: AuthController.cs
**Fichier**: `m:\win\winplus\backend\dotnet\Controllers\AuthController.cs`

**Changements**:
1. Validation complète du DTO
2. Fallback pour firstName/lastName
3. Messages d'erreur plus explicites
4. Logging détaillé des requêtes

---

## 🔐 Vérifications Frontend Nécessaires

### 1️⃣ **Vérifier que le token est stocké correctement**

```typescript
// Dans votre composant d'inscription:
async handleSignup(data: SignupData) {
    try {
        const response = await authService.signup(data);
        
        // Vérifier que le token est sauvegardé
        const token = localStorage.getItem('authToken');
        console.log('Token après signup:', token ? 'OK' : 'MANQUANT');
        
        if (!token) {
            console.error('❌ Token not saved! Check saveAuthData()');
        }
    } catch (error) {
        console.error('Signup error:', error);
    }
}
```

### 2️⃣ **Vérifier que le token est envoyé dans les requêtes**

```typescript
// Dans votre composant qui appelle getCart:
async loadCart() {
    try {
        // 1. Vérifier le token avant la requête
        const token = localStorage.getItem('authToken');
        console.log('Token disponible:', !!token);
        console.log('Token value:', token?.substring(0, 20) + '...');
        
        // 2. Appeler getCart
        const cart = await cartService.getCart();
        console.log('✅ Cart loaded:', cart);
    } catch (error: any) {
        console.error('❌ Error loading cart:', error);
        
        // 3. Vérifier le statut HTTP
        if (error.response?.status === 401) {
            console.error('Unauthorized - Token invalide ou manquant');
            // Rediriger vers login
        } else if (error.response?.status === 500) {
            console.error('Server error:', error.response.data);
        }
    }
}
```

### 3️⃣ **Vérifier l'intercepteur Axios**

```typescript
// Fichier: m:\win\winplus\frontend\src\services\api.ts
// Le code doit avoir cet interceptor:

apiClient.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('authToken');
    
    if (token && token !== 'undefined' && token.length > 0) {
      config.headers.Authorization = `Bearer ${token}`;
      console.log('✅ Authorization header added');
    } else {
      console.warn('⚠️ No token found, request will be unauthenticated');
    }
    
    return config;
  }
);
```

---

## 🧪 Test de Résolution

### Test 1: Signup avec données valides
```bash
# Réquête valide
POST http://44.200.166.163/api/auth/signup
Content-Type: application/json

{
  "firstName": "Jean",
  "lastName": "Dupont",
  "email": "jean@example.com",
  "password": "SecurePass123!",
  "confirmPassword": "SecurePass123!",
  "phone": "+33612345678"
}

# Réponse attendue: 200 OK
{
  "message": "User created successfully",
  "user": {
    "email": "jean@example.com",
    "firstName": "Jean",
    "lastName": "Dupont"
  }
}
```

### Test 2: GetCart avec token valide
```bash
# Réquête avec token
GET http://44.200.166.163/api/cart
Authorization: Bearer <YOUR_JWT_TOKEN>

# Réponse attendue: 200 OK
{
  "items": [],
  "itemsCount": 0,
  "subtotal": 0,
  "discount": 0,
  "tax": 0,
  "total": 0,
  "currency": "XAF",
  "updatedAt": "2026-02-03T..."
}
```

### Test 3: GetCart sans token (doit retourner 401, pas 500)
```bash
# Réquête sans token
GET http://44.200.166.163/api/cart

# Réponse attendue: 401 Unauthorized
{
  "error": "Authentication required"
}
```

---

## 📊 Logs de Débogage

### Activer les logs détaillés dans le backend

**Fichier**: `m:\win\winplus\backend\dotnet\appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information",
      "Backend.Controllers": "Debug",
      "Backend.Services": "Debug"
    }
  }
}
```

### Vérifier les logs dans la console:
- ✅ `Token validated for Cognito ID: xxx`
- ✅ `SignUp attempt for email: xxx, firstName: xxx, lastName: xxx`
- ❌ `Failed to extract userId from token claims`
- ❌ `SignUp failed for email: xxx. Message: xxx`

---

## ✅ Checklist de Vérification

- [ ] Token est sauvegardé après signup
- [ ] Token est transmis dans le header `Authorization: Bearer`
- [ ] Backend reçoit les données firstname/lastname
- [ ] CartController retourne 401 (pas 500) si pas authentifié
- [ ] SignUpRequestDto accepte les données du frontend
- [ ] Cognito reçoit les bonnes données d'inscription
- [ ] Les logs backend affichent les détails des requêtes
- [ ] La réponse 500 est remplacée par 401/400 approprié

---

## 🚀 Prochaines Étapes

1. **Redémarrer le backend** pour appliquer les corrections
2. **Vider le cache** du navigateur (Ctrl+Shift+Del)
3. **Tester l'inscription** avec des données valides
4. **Vérifier les logs** pour identifier les vraies erreurs
5. **Adapter le frontend** si nécessaire pour envoyer les données correctement

---

## 📞 Support Supplémentaire

Si les erreurs persistent:
1. Vérifiez les logs complets du serveur backend (docker/application)
2. Utilisez Postman pour tester les endpoints directement
3. Vérifiez la configuration AWS Cognito (UserPoolId, ClientId)
4. Vérifiez la chaîne de connexion PostgreSQL
