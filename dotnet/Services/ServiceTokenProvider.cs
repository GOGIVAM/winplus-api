using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Services;

/// <summary>
/// Jeton technique pour les appels .NET → FastAPI depuis une tâche de fond
/// (Modules 23 et 36).
///
/// Les BackgroundService n'ont pas de requête HTTP entrante, donc aucun jeton
/// utilisateur à relayer : leurs appels partaient sans Authorization, FastAPI
/// répondait 401 et l'échec était avalé par un repli silencieux.
///
/// Règles, alignées sur python/auth.py (require_user_or_service) :
///  - même secret que les jetons utilisateurs (JWT:SecretKey, déjà requis au
///    démarrage) : aucun nouveau secret à positionner ;
///  - audience dédiée <see cref="ServiceAudience"/> : ni le JwtBearer .NET
///    (qui exige l'audience utilisateur) ni verify_token côté Python ne
///    l'acceptent. Il n'ouvre que les routes Python qui déclarent explicitement
///    le périmètre demandé ;
///  - un périmètre (claim « scope ») par tâche de fond, pas de jeton passe-partout ;
///  - durée de vie de 5 minutes, émis juste avant chaque appel.
/// </summary>
public interface IServiceTokenProvider
{
    /// <summary>Valeur complète de l'en-tête Authorization ("Bearer …") pour le périmètre donné.</summary>
    string CreateAuthorizationHeader(string scope);
}

/// <summary>Périmètres reconnus côté Python (voir les Depends(require_user_or_service(...))).</summary>
public static class ServiceScopes
{
    public const string Notification = "ai.notification";           // /api/ai/generate-notification
    public const string CoachingReport = "ai.coaching-report";      // /api/teacher/coaching-report
    public const string InstitutionReport = "ai.institution-report"; // /api/institution/action-plan
    public const string ParentReport = "ai.parent-report";          // /api/chatbot/chat
    public const string Decrochage = "ai.decrochage";               // /api/winai/detection-decrochage
    public const string ParentAdvisorThresholds = "ai.parent-advisor-thresholds"; // /api/parent-advisor/threshold-alerts
    public const string ParentWeeklyTrend = "ai.parent-weekly-trend";             // /api/parent-advisor/weekly-trend
}

public class ServiceTokenProvider : IServiceTokenProvider
{
    public const string ServiceAudience = "WinPlusAIService";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;

    public ServiceTokenProvider(IConfiguration configuration)
    {
        var secretKey = configuration["JWT:SecretKey"]
            ?? throw new InvalidOperationException("JWT:SecretKey not configured");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        _issuer = configuration["JWT:Issuer"] ?? "WinPlusApp";
    }

    public string CreateAuthorizationHeader(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Un périmètre est obligatoire pour un jeton technique.", nameof(scope));

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: ServiceAudience,
            claims: new[]
            {
                new Claim("sub", "service:winplus-dotnet"),
                new Claim("token_use", "service"),
                new Claim("scope", scope),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: now,
            expires: now.Add(Lifetime),
            // HmacSha256 (forme compacte "HS256"), comme JwtService : PyJWT
            // refuse l'URI XML-dsig de HmacSha256Signature.
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return "Bearer " + new JwtSecurityTokenHandler().WriteToken(token);
    }
}
