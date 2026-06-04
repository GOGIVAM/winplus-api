# 🛠️ CODE CORRECTIONS - PRÊT À COPIER-COLLER

**Projet:** Winplus Backend  
**Objectif:** Corrections des 5 problèmes CRITIQUES

---

## 🔴 CORRECTION #1: SUPPRIMER DUAL AUTH

### Étape 1: Supprimer fichiers legacy

```bash
# Dans le dossier backend/Services/
rm SimpleAuthService.cs
rm EmailService.cs
```

### Étape 2: Vérifier Program.cs

```csharp
// Program.cs - VÉRIFIER que cette ligne existe:
builder.Services.AddScoped<ICognitoAuthService, CognitoAuthService>();

// ❌ SUPPRIMER si présent:
// builder.Services.AddScoped<ISimpleAuthService, SimpleAuthService>();
```

---

## 🔴 CORRECTION #2: HARDCODED USER IDS

### Fichier 1: Créer extension (NOUVEAU FICHIER)

**Chemin:** `backend/Extensions/ClaimsPrincipalExtensions.cs`

```csharp
using System.Security.Claims;

namespace Backend.Extensions;

/// <summary>
/// Extensions pour extraire les informations utilisateur des claims JWT
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extrait l'ID utilisateur du token JWT
    /// </summary>
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        // Chercher dans différents claims possibles
        var userIdClaim = principal.FindFirst("user_id") 
                       ?? principal.FindFirst("sub")
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier);
        
        if (userIdClaim == null)
        {
            throw new UnauthorizedAccessException("Token does not contain user_id claim");
        }
        
        if (!int.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException($"Invalid user_id claim value: {userIdClaim.Value}");
        }
        
        return userId;
    }
    
    /// <summary>
    /// Extrait le rôle utilisateur du token JWT
    /// </summary>
    public static string GetUserRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("role")?.Value 
            ?? principal.FindFirst(ClaimTypes.Role)?.Value 
            ?? "student";
    }
    
    /// <summary>
    /// Extrait l'email utilisateur du token JWT
    /// </summary>
    public static string GetUserEmail(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("email")?.Value 
            ?? principal.FindFirst(ClaimTypes.Email)?.Value 
            ?? string.Empty;
    }
    
    /// <summary>
    /// Vérifie si l'utilisateur a un rôle spécifique
    /// </summary>
    public static bool HasRole(this ClaimsPrincipal principal, string role)
    {
        var userRole = principal.GetUserRole();
        return userRole.Equals(role, StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Vérifie si l'utilisateur est admin
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal principal)
    {
        return principal.HasRole("admin");
    }
}
```

### Fichier 2: UsersController.cs - REMPLACER

**Chemin:** `backend/Controllers/UsersController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Extensions; // ✅ AJOUTER

namespace Backend.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userId = User.GetUserId(); // ✅ CORRIGÉ
            
            _logger.LogInformation("Getting profile for user {UserId}", userId);
            
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound(new { error = "User not found" });
                
            return Ok(new
            {
                success = true,
                data = user,
                timestamp = DateTime.UtcNow
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Unauthorized profile access: {Message}", ex.Message);
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user profile");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] User user)
    {
        try
        {
            var userId = User.GetUserId(); // ✅ CORRIGÉ
            
            // Vérifier que l'user modifie son propre profil
            if (user.Id != userId)
            {
                return Forbid("You can only update your own profile");
            }
            
            var updated = await _userService.UpdateUserAsync(user);
            return Ok(new
            {
                success = true,
                data = updated,
                message = "Profile updated successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user profile");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var users = await _userService.GetAllUsersAsync();
            return Ok(new
            {
                success = true,
                data = users,
                count = users.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all users");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _userService.DeleteUserAsync(id);
            if (!result)
                return NotFound(new { error = "User not found" });
                
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
```

### Fichier 3-11: APPLIQUER LA MÊME CORRECTION

**Pour CHAQUE controller suivant, remplacer:**
```csharp
// ❌ AVANT:
var userId = 1; // À remplacer par l'ID de l'utilisateur connecté

// ✅ APRÈS:
var userId = User.GetUserId();
```

**Fichiers à modifier:**
1. ✅ `Controllers/OrdersController.cs`
2. ✅ `Controllers/CartController.cs`
3. ✅ `Controllers/EnrollmentsController.cs`
4. ✅ `Controllers/PaymentsController.cs`
5. ✅ `Controllers/HistoryController.cs`
6. ✅ `Controllers/AnalyticsController.cs`
7. ✅ `Controllers/FavoritesController.cs`

### Fichier 12: ClaimsEnricher.cs - MODIFIER

**Chemin:** `backend/Utilities/ClaimsEnricher.cs`

```csharp
using System.Security.Claims;
using Backend.Repositories;
using Microsoft.Extensions.Logging;

namespace Backend.Utilities;

public interface IClaimsEnricher
{
    Task<ClaimsPrincipal> EnrichClaimsAsync(ClaimsPrincipal principal);
}

public class ClaimsEnricher : IClaimsEnricher
{
    private readonly IUserRepository _userRepository;
    private readonly ILogger<ClaimsEnricher> _logger;

    public ClaimsEnricher(IUserRepository userRepository, ILogger<ClaimsEnricher> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal> EnrichClaimsAsync(ClaimsPrincipal principal)
    {
        try
        {
            // Extraire Cognito ID (sub claim)
            var cognitoId = principal.FindFirst("sub")?.Value;
            
            if (string.IsNullOrEmpty(cognitoId))
            {
                _logger.LogWarning("No Cognito ID found in token");
                return principal;
            }
            
            // Chercher user dans DB
            var user = await _userRepository.GetUserByCognitoIdAsync(cognitoId);
            
            if (user == null)
            {
                _logger.LogWarning("User not found for Cognito ID: {CognitoId}", cognitoId);
                return principal;
            }
            
            // Enrichir avec claims custom
            var claims = new List<Claim>
            {
                new Claim("user_id", user.Id.ToString()),                      // ✅ ESSENTIEL
                new Claim("role", user.Role ?? "student"),                     // ✅ ESSENTIEL
                new Claim("email", user.Email),
                new Claim("email_verified", user.IsEmailVerified.ToString().ToLower()),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim(ClaimTypes.GivenName, user.FirstName ?? ""),
                new Claim(ClaimTypes.Surname, user.LastName ?? "")
            };
            
            _logger.LogInformation(
                "Enriched claims for user {UserId} (role: {Role})", 
                user.Id, user.Role);
            
            // Créer nouvelle identité avec claims enrichis
            var identity = new ClaimsIdentity(claims, "Enriched");
            var newPrincipal = new ClaimsPrincipal(identity);
            
            // Ajouter identités existantes
            newPrincipal.AddIdentities(principal.Identities);
            
            return newPrincipal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching claims");
            return principal;
        }
    }
}
```

---

## 🔴 CORRECTION #3: ADMIN AUTHORIZATION

### Fichier 1: Migration (NOUVEAU FICHIER)

**Chemin:** `backend/Migrations/20260119_AddUserRole.cs`

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ajouter colonne Role
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "student");
            
            // Index pour performance
            migrationBuilder.CreateIndex(
                name: "IX_Users_Role",
                table: "Users",
                column: "Role");
            
            // Assigner rôle admin au premier utilisateur
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"Role\" = 'admin' WHERE \"Id\" = 1;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Role",
                table: "Users");
                
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");
        }
    }
}
```

### Fichier 2: User.cs - AJOUTER PROPRIÉTÉ

**Chemin:** `backend/Models/Entities/User.cs`

```csharp
// Dans la classe User, AJOUTER:

[MaxLength(50)]
public string Role { get; set; } = "student";
```

### Fichier 3: Program.cs - CORRIGER POLICIES

**Chemin:** `backend/Program.cs`

```csharp
// TROUVER la section AddAuthorization et REMPLACER par:

builder.Services.AddAuthorization(options =>
{
    // Policy Admin - CRITIQUE: doit vérifier le role
    options.AddPolicy("AdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", "admin");  // ✅ AJOUTER
    });
    
    // Policy Teacher
    options.AddPolicy("TeacherOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", "teacher", "admin");
    });
    
    // Policy Parent
    options.AddPolicy("ParentOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", "parent", "admin");
    });
    
    // Policy Student
    options.AddPolicy("StudentOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", "student", "teacher", "parent", "admin");
    });
});
```

### Commandes à exécuter:

```bash
# 1. Créer migration
dotnet ef migrations add AddUserRole

# 2. Appliquer à la DB
dotnet ef database update

# 3. Vérifier
dotnet ef migrations list
# Dernière migration doit être: AddUserRole ✅
```

---

## 🔴 CORRECTION #4: FLASK SÉCURITÉ

### Fichier 1: auth.py (NOUVEAU FICHIER)

**Chemin:** `backend/fastapi_api/auth.py`

```python
#!/usr/bin/env python3
"""
Middleware d'authentification JWT pour FastApi
Partage le même secret que .NET backend
"""

import jwt
import os
from functools import wraps
from fastapi import request, jsonify
import logging

logger = logging.getLogger(__name__)

# Configuration JWT (même que .NET)
JWT_SECRET = os.getenv('JWT_SECRET_KEY', 'your-secret-key-must-match-dotnet')
JWT_ALGORITHM = 'HS256'

def require_auth(f):
    """
    Decorator pour protéger les endpoints FastApi
    Usage: @require_auth
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        token = None
        
        # Extraire token du header Authorization
        if 'Authorization' in request.headers:
            auth_header = request.headers['Authorization']
            try:
                # Format: "Bearer <token>"
                parts = auth_header.split(" ")
                if len(parts) == 2 and parts[0] == "Bearer":
                    token = parts[1]
                else:
                    return jsonify({
                        'error': 'Invalid authorization header format',
                        'message': 'Use: Authorization: Bearer <token>'
                    }), 401
            except Exception as e:
                logger.error(f"Error parsing authorization header: {e}")
                return jsonify({'error': 'Invalid token format'}), 401
        
        if not token:
            return jsonify({
                'error': 'missing_token',
                'message': 'Authentication token is required'
            }), 401
        
        try:
            # Valider JWT avec même secret que .NET
            payload = jwt.decode(
                token,
                JWT_SECRET,
                algorithms=[JWT_ALGORITHM]
            )
            
            # Extraire informations utilisateur
            request.user_id = payload.get('user_id')
            request.user_role = payload.get('role', 'student')
            request.cognito_id = payload.get('sub')
            request.user_email = payload.get('email')
            
            logger.info(f"Authenticated request from user_id={request.user_id}, role={request.user_role}")
            
            # Vérifier que user_id est présent
            if not request.user_id:
                return jsonify({
                    'error': 'invalid_token',
                    'message': 'Token does not contain user_id'
                }), 401
            
        except jwt.ExpiredSignatureError:
            logger.warning("Token expired")
            return jsonify({
                'error': 'token_expired',
                'message': 'Your session has expired, please login again'
            }), 401
        except jwt.InvalidTokenError as e:
            logger.error(f"Invalid token: {e}")
            return jsonify({
                'error': 'invalid_token',
                'message': 'Authentication token is invalid'
            }), 401
        except Exception as e:
            logger.error(f"Token validation error: {e}")
            return jsonify({
                'error': 'auth_error',
                'message': 'Authentication failed'
            }), 500
        
        return f(*args, **kwargs)
    
    return decorated_function


def require_role(*allowed_roles):
    """
    Decorator pour vérifier le rôle utilisateur
    Usage: @require_role('admin', 'teacher')
    """
    def decorator(f):
        @wraps(f)
        @require_auth
        def decorated_function(*args, **kwargs):
            user_role = getattr(request, 'user_role', 'student')
            
            if user_role not in allowed_roles:
                return jsonify({
                    'error': 'forbidden',
                    'message': f'Access forbidden. Required roles: {", ".join(allowed_roles)}'
                }), 403
            
            return f(*args, **kwargs)
        return decorated_function
    return decorator
```

### Fichier 2: app.py - MODIFIER

**Chemin:** `backend/fastapi_api/app.py`

```python
#!/usr/bin/env python3
"""
Service IA FastApi avec authentification
"""

from fastapi import FastApi, request, jsonify
from fastapi_cors import CORS
from fastapi_limiter import Limiter
from fastapi_limiter.util import get_remote_address
from dotenv import load_dotenv
import os
import logging

from database import Database, init_db
from models.nlp_analyzer import NLPAnalyzer
from models.recommender import Recommender
from auth import require_auth, require_role  # ✅ IMPORTER

# Configuration
load_dotenv()
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastApi(__name__)
CORS(app)

# ✅ AJOUTER Rate Limiting
limiter = Limiter(
    app=app,
    key_func=get_remote_address,
    default_limits=["200 per day", "50 per hour"],
    storage_uri="memory://"  # Ou Redis: "redis://localhost:6379"
)

# Initialisation
db = Database()
nlp_analyzer = NLPAnalyzer()
recommender = Recommender(db)

# ==================== HEALTH CHECK (Public) ====================
@app.route('/health', methods=['GET'])
def health():
    """Health check - pas d'auth requise"""
    return jsonify({
        'status': 'healthy',
        'service': 'IA Educational Service',
        'version': '1.0.0'
    })

# ==================== NLP ANALYSIS ====================
@app.route('/api/v1/analyze_content', methods=['POST'])
@limiter.limit("20 per minute")  # ✅ AJOUTER
@require_auth                     # ✅ AJOUTER
def analyze_content():
    """Analyse NLP - protégé par auth"""
    try:
        data = request.get_json()
        
        if 'content_id' in data:
            content = db.get_content_by_id(data['content_id'])
            if not content:
                return jsonify({'error': 'Content not found'}), 404
            
            text = f"{content['titre']}. {content['description']}"
            metadata = {'theme': content['theme']}
        
        elif 'text' in data:
            text = data['text']
            metadata = {'title': data.get('title', 'Untitled')}
        
        else:
            return jsonify({'error': 'Missing content_id or text'}), 400
        
        result = nlp_analyzer.analyze(text, metadata)
        
        logger.info(f"Content analyzed by user {request.user_id}")
        
        return jsonify(result), 200
    
    except Exception as e:
        logger.error(f"Error in analyze_content: {e}")
        return jsonify({'error': str(e)}), 500

# ==================== RECOMMANDATIONS ====================
@app.route('/api/v1/recommendations', methods=['GET'])
@limiter.limit("10 per minute")  # ✅ AJOUTER
@require_auth                     # ✅ AJOUTER
def get_recommendations():
    """Recommandations basiques - protégé"""
    try:
        user_id = request.user_id  # ✅ Du token, pas de query param
        limit = int(request.args.get('limit', 10))
        
        recommendations = recommender.recommend(user_id, limit)
        
        return jsonify({
            'user_id': user_id,
            'recommendations': recommendations,
            'count': len(recommendations)
        }), 200
    
    except Exception as e:
        logger.error(f"Error in recommendations: {e}")
        return jsonify({'error': str(e)}), 500

# ==================== RECOMMANDATIONS PERSONNALISÉES ====================
@app.route('/api/v1/recommendations/personalized', methods=['POST'])
@limiter.limit("10 per minute")  # ✅ AJOUTER
@require_auth                     # ✅ AJOUTER
def get_personalized_recommendations():
    """Recommandations filtrées - protégé"""
    try:
        data = request.get_json()
        user_id = request.user_id  # ✅ Du token
        
        theme = data.get('theme')
        difficulty_range = data.get('difficulty_range', [0, 1])
        limit = data.get('limit', 10)
        
        recommendations = recommender.recommend_filtered(
            user_id, theme, difficulty_range, limit
        )
        
        return jsonify({
            'user_id': user_id,
            'recommendations': recommendations,
            'filters': {
                'theme': theme,
                'difficulty_range': difficulty_range
            },
            'count': len(recommendations)
        }), 200
    
    except Exception as e:
        logger.error(f"Error in personalized recommendations: {e}")
        return jsonify({'error': str(e)}), 500

# ==================== STATS UTILISATEUR ====================
@app.route('/api/v1/users/<int:user_id>/stats', methods=['GET'])
@limiter.limit("30 per minute")  # ✅ AJOUTER
@require_auth                     # ✅ AJOUTER
def get_user_stats(user_id):
    """Stats utilisateur - vérification propriétaire"""
    try:
        # Vérifier que l'user accède à ses propres stats
        # ou que c'est un admin
        if request.user_id != user_id and request.user_role != 'admin':
            return jsonify({
                'error': 'forbidden',
                'message': 'You can only access your own statistics'
            }), 403
        
        user = db.get_user_by_id(user_id)
        if not user:
            return jsonify({'error': 'User not found'}), 404
        
        stats = db.get_user_stats(user_id)
        
        return jsonify({
            'user_id': user_id,
            'profile': {
                'nom': user['nom'],
                'prenom': user['prenom'],
                'age': user['age'],
                'niveau': user['niveau'],
                'objectif': user['objectif']
            },
            'statistics': stats
        }), 200
    
    except Exception as e:
        logger.error(f"Error in user stats: {e}")
        return jsonify({'error': str(e)}), 500

# ==================== MAIN ====================
if __name__ == '__main__':
    logger.info("Initialisation de la base de données...")
    init_db()
    
    logger.info("Chargement du modèle NLP...")
    nlp_analyzer.load_model()
    
    port = int(os.getenv('FLASK_PORT', 5000))
    debug = os.getenv('FLASK_DEBUG', 'True').lower() == 'true'
    
    logger.info(f"🚀 Serveur FastApi démarré sur http://localhost:{port}")
    logger.info(f"✅ Authentification JWT activée")
    logger.info(f"✅ Rate limiting activé")
    
    app.run(host='0.0.0.0', port=port, debug=debug)
```

### Fichier 3: .env

**Chemin:** `backend/fastapi_api/.env`

```bash
# JWT Configuration (DOIT MATCHER .NET)
JWT_SECRET_KEY=your-shared-secret-key-between-dotnet-and-fastapi

# FastApi Config
FLASK_PORT=5000
FLASK_DEBUG=True

# Database
DB_TYPE=postgresql
DB_HOST=localhost
DB_PORT=5432
DB_NAME=winplus_db
DB_USER=postgres
DB_PASSWORD=your_password
```

### Fichier 4: requirements.txt - AJOUTER

**Chemin:** `backend/fastapi_api/requirements.txt`

```txt
# Ajouter ces lignes:
fastapi-limiter==3.5.0
PyJWT==2.8.0
```

### Installation:

```bash
cd backend/fastapi_api
source venv/bin/activate
pip install fastapi-limiter PyJWT
pip freeze > requirements.txt
```

---

## 🔴 CORRECTION #5: CORS

### Program.cs - REMPLACER SECTION CORS

**Chemin:** `backend/Program.cs`

```csharp
// TROUVER la section AddCors et REMPLACER par:

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", builder =>
    {
        // Charger origins depuis configuration
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? new[]
            {
                "http://localhost:3000",
                "http://localhost:5173"
            };
        
        builder
            .WithOrigins(allowedOrigins)     // ✅ Origins spécifiques
            .AllowCredentials()               // ✅ Pour cookies/auth
            .AllowMethods("GET", "POST", "PUT", "DELETE", "PATCH")
            .AllowHeaders("Content-Type", "Authorization", "X-Requested-With")
            .WithExposedHeaders("Content-Disposition"); // Pour downloads
    });
});

// TROUVER app.UseCors et REMPLACER par:
app.UseCors("AllowFrontend");  // ✅ Pas "AllowAll"
```

### appsettings.json - AJOUTER SECTION

**Chemin:** `backend/appsettings.json`

```json
{
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000",
      "http://localhost:5173"
    ]
  }
}
```

### appsettings.Production.json - AJOUTER SECTION

**Chemin:** `backend/appsettings.Production.json`

```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://winplus.com",
      "https://www.winplus.com",
      "https://winplus-staging.com"
    ]
  }
}
```

---

## ✅ VÉRIFICATION POST-CORRECTIONS

### Tests à exécuter:

```bash
# 1. Compiler .NET
cd backend
dotnet build
# Attendu: 0 Error(s) ✅

# 2. Appliquer migrations
dotnet ef database update
# Attendu: Migration AddUserRole appliquée ✅

# 3. Tester extraction userId
curl -H "Authorization: Bearer {your_jwt_token}" \
  http://localhost:5001/api/users/profile
# Attendu: Profil retourné, logs montrent userId correct ✅

# 4. Tester admin
curl -H "Authorization: Bearer {student_token}" \
  http://localhost:5001/api/admin/users
# Attendu: 403 Forbidden ✅

curl -H "Authorization: Bearer {admin_token}" \
  http://localhost:5001/api/admin/users
# Attendu: 200 OK + liste users ✅

# 5. Tester FastApi auth
curl http://localhost:5000/api/v1/recommendations
# Attendu: 401 Missing token ✅

curl -H "Authorization: Bearer {token}" \
  http://localhost:5000/api/v1/recommendations
# Attendu: 200 OK + recommendations ✅

# 6. Tester CORS
curl -H "Origin: http://evil.com" \
  http://localhost:5001/api/subjects
# Attendu: CORS error (origin not allowed) ✅

curl -H "Origin: http://localhost:3000" \
  http://localhost:5001/api/subjects
# Attendu: 200 OK ✅
```

---

## 📋 CHECKLIST FINALE

**Après avoir appliqué TOUTES les corrections:**

- [ ] SimpleAuthService.cs supprimé
- [ ] EmailService.cs supprimé
- [ ] ClaimsPrincipalExtensions.cs créé
- [ ] Tous les controllers utilisent User.GetUserId()
- [ ] Migration AddUserRole créée et appliquée
- [ ] User.Role propriété ajoutée
- [ ] AdminOnly policy avec RequireClaim
- [ ] ClaimsEnricher enrichit user_id + role
- [ ] auth.py FastApi créé
- [ ] app.py FastApi avec @require_auth
- [ ] fastapi-limiter + PyJWT installés
- [ ] CORS restrictif configuré
- [ ] appsettings.json avec Cors section
- [ ] Tests de vérification passent ✅

**Résultat attendu:** 5 failles critiques corrigées ✅

---

FIN DU FICHIER DE CORRECTIONS
