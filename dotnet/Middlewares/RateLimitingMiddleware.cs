using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Backend.Middlewares;

/// <summary>
/// Rate limiting middleware for authentication endpoints.
///
/// Module 20 : ce composant était contournable en une ligne. L'en-tête
/// X-Forwarded-For était accepté sans validation alors que les listes de
/// proxies et de réseaux de confiance sont vidées au démarrage : il suffisait
/// de changer l'en-tête à chaque requête pour obtenir un compteur neuf, ce qui
/// autorisait une force brute illimitée sur la connexion. Le registre de
/// requêtes n'était par ailleurs jamais purgé de ses clés mortes, ce qui
/// transformait cette attaque en fuite mémoire, et les limites étaient restées
/// aux valeurs de test.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // Store requests per IP: IP -> (timestamp, count)
    private static readonly Dictionary<string, List<DateTime>> RequestLog = new();
    private static readonly object LockObject = new();
    private static DateTime _lastSweepUtc = DateTime.UtcNow;

    /// <summary>
    /// Au-delà de cette durée sans requête, la clé d'une adresse est retirée
    /// du registre. Sans cette purge, chaque adresse vue une seule fois
    /// restait en mémoire pour la vie du processus.
    /// </summary>
    private static readonly TimeSpan KeyRetention = TimeSpan.FromHours(2);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    // Limites de production. Les valeurs précédentes (20 / 50 / 30) avaient
    // été relevées pour les tests et laissées telles quelles.
    private const int LoginAttempts = 8;
    private const int LoginWindowMinutes = 15;
    private const int SignupAttempts = 10;
    private const int SignupWindowHours = 1;
    private const int PasswordResetAttempts = 6;
    private const int PasswordResetWindowHours = 1;

    /// <summary>
    /// Endpoints sensibles non couverts jusqu'ici : vérification d'adresse,
    /// renvoi de vérification, réinitialisation de mot de passe et
    /// rafraîchissement de jeton. Chacun permet soit d'énumérer des comptes,
    /// soit de tenter un code à l'aveugle, soit d'entretenir une session.
    /// </summary>
    private const int SensitiveAttempts = 12;
    private const int SensitiveWindowMinutes = 15;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value ?? "";
        var clientIp = GetClientIpAddress(context);

        var rule = ResolveRule(path);
        if (rule != null)
        {
            var (maxAttempts, windowMinutes, label, retryAfter) = rule.Value;
            if (!CheckRateLimit(clientIp, label, maxAttempts, windowMinutes))
            {
                _logger.LogWarning("Rate limit exceeded for {Label} from IP: {ClientIp}", label, clientIp);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = $"Too many {label} attempts. Please try again later.",
                    retryAfter
                });
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Toutes les routes d'une même action partagent la même règle et le même
    /// compteur. Passe de clôture du lot 0 : l'alias <c>/auth/register</c>
    /// (même action que <c>/auth/signup</c> dans AuthController) échappait à la
    /// limite d'inscription. Relevé exhaustif des routes sensibles de
    /// AuthController : signup, register, signin, forgot-password,
    /// verify-reset-token, reset-password, verify-email, resend-verification,
    /// refresh, send-confirmation-code, verify-confirmation. Toute nouvelle
    /// route ou tout nouvel alias d'une action sensible doit être ajouté ici.
    /// </summary>
    private static readonly (string[] Segments, int MaxAttempts, int WindowMinutes, string Label, int RetryAfter)[] Rules =
    {
        (new[] { "/auth/signin" },
            LoginAttempts, LoginWindowMinutes, "login", LoginWindowMinutes),
        (new[] { "/auth/signup", "/auth/register" },
            SignupAttempts, SignupWindowHours * 60, "signup", SignupWindowHours),
        (new[] { "/auth/forgot-password" },
            PasswordResetAttempts, PasswordResetWindowHours * 60, "password reset", PasswordResetWindowHours),
        (new[]
            {
                "/auth/reset-password", "/auth/verify-reset-token",
                "/auth/verify-email", "/auth/resend-verification", "/auth/refresh",
                "/auth/send-confirmation-code", "/auth/verify-confirmation"
            },
            SensitiveAttempts, SensitiveWindowMinutes, "authentication", SensitiveWindowMinutes),
    };

    private static (int MaxAttempts, int WindowMinutes, string Label, int RetryAfter)? ResolveRule(string path)
    {
        foreach (var rule in Rules)
        {
            if (rule.Segments.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return (rule.MaxAttempts, rule.WindowMinutes, rule.Label, rule.RetryAfter);
        }
        return null;
    }

    /// <summary>
    /// Le compteur est tenu par couple (règle, adresse) : les alias d'une même
    /// action tombent dans le même compteur, et une règle ne consomme plus le
    /// quota d'une autre (auparavant toutes les règles partageaient une seule
    /// liste par adresse, si bien que des rafraîchissements de jeton pouvaient
    /// bloquer la connexion).
    /// </summary>
    private bool CheckRateLimit(string clientIp, string label, int maxAttempts, int windowMinutes)
    {
        lock (LockObject)
        {
            var now = DateTime.UtcNow;
            SweepStaleKeys(now);

            var cutoffTime = now.AddMinutes(-windowMinutes);
            var key = $"{label}|{clientIp}";

            if (!RequestLog.TryGetValue(key, out var entries))
            {
                entries = new List<DateTime>();
                RequestLog[key] = entries;
            }

            entries.RemoveAll(t => t <= cutoffTime);

            if (entries.Count >= maxAttempts)
                return false;

            entries.Add(now);
            return true;
        }
    }

    /// <summary>
    /// Purge périodique du registre : sans elle, chaque adresse vue une fois
    /// restait indéfiniment en mémoire, ce qu'une attaque changeant
    /// l'en-tête d'adresse à chaque requête amplifiait sans limite.
    /// Appelée sous le verrou déjà pris par CheckRateLimit.
    /// </summary>
    private static void SweepStaleKeys(DateTime now)
    {
        if (now - _lastSweepUtc < SweepInterval) return;
        _lastSweepUtc = now;

        var retentionCutoff = now - KeyRetention;
        var dead = RequestLog
            .Where(kv => kv.Value.Count == 0 || kv.Value.Max() <= retentionCutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in dead)
            RequestLog.Remove(key);
    }

    /// <summary>
    /// Adresse du client.
    ///
    /// L'en-tête transmis par un intermédiaire n'est PLUS lu ici. Il est
    /// désormais traité en amont du pipeline par UseForwardedHeaders, qui ne
    /// l'applique que si la connexion entrante provient d'un relais de
    /// confiance déclaré (voir Program.cs) : l'adresse de connexion est donc
    /// la vraie adresse du client derrière un relais légitime, et reste celle
    /// de l'appelant direct sinon. Lire l'en-tête une seconde fois ici
    /// annulerait exactement cette validation, c'était la faille d'origine.
    /// </summary>
    private string GetClientIpAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
}

/// <summary>
/// Extension method for adding rate limiting middleware
/// </summary>
public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitingMiddleware>();
    }
}
