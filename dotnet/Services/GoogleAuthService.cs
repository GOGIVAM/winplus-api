using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Issue d'une connexion Google : soit une session ouverte, soit le rôle à
/// demander avant de créer le compte (personne inconnue venue de la page de
/// connexion), soit un refus.
/// </summary>
public sealed record GoogleSignInOutcome(
    AuthResult? Session,
    bool NeedsRole = false,
    string? IdToken = null,
    string? Email = null,
    string? FirstName = null,
    string? ErrorCode = null);

public interface IGoogleAuthService
{
    /// <summary>Identifiant client public, ou null si Google n'est pas configuré.</summary>
    string? ClientId { get; }

    /// <summary>
    /// Premier passage : <paramref name="code"/> et <paramref name="redirectUri"/>
    /// renvoyés par Google. Second passage (rôle choisi) : <paramref name="idToken"/>
    /// rendu au premier passage, et <paramref name="role"/>.
    /// </summary>
    Task<GoogleSignInOutcome> SignInAsync(
        string? code, string? redirectUri, string? idToken, string? role, string? locale,
        HttpRequest request, CancellationToken ct = default);
}

/// <summary>
/// Connexion avec Google (OpenID Connect, flux « code d'autorisation ») :
///  1. le code est échangé contre un jeton d'identité, avec le secret client ;
///  2. le jeton est vérifié (signature avec les clés publiques de Google,
///     émetteur, destinataire = notre identifiant client, expiration) ;
///  3. le compte est retrouvé par son lien Google, sinon par son adresse
///     vérifiée (il est alors relié), sinon créé avec le rôle choisi ;
///  4. la session s'ouvre comme une connexion classique.
/// Identifiants : OAuth:Google:ClientId et OAuth:Google:ClientSecret, fournis
/// par variables d'environnement (OAuth__Google__ClientId…), jamais dans le dépôt.
/// </summary>
public class GoogleAuthService : IGoogleAuthService
{
    private const string Provider = "google";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    // Adresses de retour acceptées si la configuration n'en donne pas : la page
    // /auth/google/callback de chaque environnement. Une adresse hors liste est
    // refusée, Google l'exigerait de toute façon.
    private static readonly string[] DefaultRedirectUris =
    {
        "https://winplus.cm/auth/google/callback",
        "https://www.winplus.cm/auth/google/callback",
        "https://integration.winplus.cm/auth/google/callback",
        "http://localhost:5173/auth/google/callback",
    };

    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "student", "teacher", "parent", "institution"
    };

    // Clés publiques de Google, mises en cache et renouvelées automatiquement.
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> GoogleOidc = new(
        "https://accounts.google.com/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = true });

    private readonly ApplicationDbContext _db;
    private readonly ICustomAuthService _auth;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<GoogleAuthService> _logger;
    private readonly IPlatformSettings? _platformSettings;
    private readonly string? _clientSecret;
    private readonly HashSet<string> _redirectUris;

    public string? ClientId { get; }

    public GoogleAuthService(
        ApplicationDbContext db,
        ICustomAuthService auth,
        IHttpClientFactory http,
        IConfiguration configuration,
        ILogger<GoogleAuthService> logger,
        IPlatformSettings? platformSettings = null)
    {
        _db = db;
        _auth = auth;
        _http = http;
        _logger = logger;
        _platformSettings = platformSettings;
        var section = configuration.GetSection("OAuth:Google");
        var id = section["ClientId"];
        var secret = section["ClientSecret"];
        ClientId = string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : id.Trim();
        _clientSecret = secret?.Trim();
        var configured = section.GetSection("AllowedRedirectUris").Get<string[]>();
        _redirectUris = new HashSet<string>(
            configured is { Length: > 0 } ? configured : DefaultRedirectUris, StringComparer.Ordinal);
    }

    public async Task<GoogleSignInOutcome> SignInAsync(
        string? code, string? redirectUri, string? idToken, string? role, string? locale,
        HttpRequest request, CancellationToken ct = default)
    {
        if (ClientId == null) return Fail("google_not_configured");

        // 1. Jeton d'identité : échangé contre le code, ou rendu au second passage.
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (string.IsNullOrWhiteSpace(redirectUri) || !_redirectUris.Contains(redirectUri))
                return Fail("google_bad_redirect");
            idToken = await ExchangeCodeAsync(code, redirectUri, ct);
            if (idToken == null) return Fail("google_exchange_failed");
        }
        if (string.IsNullOrWhiteSpace(idToken)) return Fail("google_missing_code");

        // 2. Vérification du jeton.
        var identity = await ValidateIdTokenAsync(idToken, ct);
        if (identity == null) return Fail("google_invalid_token");
        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
            return Fail("google_email_unverified");

        // 3. Compte : lien Google, puis adresse, puis création.
        var link = await _db.OAuthAccounts.Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Provider == Provider && a.ProviderUserId == identity.Subject && a.IsActive, ct);
        var user = link?.User;

        if (user == null)
        {
            var email = identity.Email.Trim().ToLowerInvariant();
            user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
            if (user != null)
            {
                // Compte existant : Google prouve que la personne possède cette
                // adresse. Un compte jamais vérifié a pu être créé par quelqu'un
                // d'autre avec cette adresse : son mot de passe est alors effacé
                // (« Mot de passe oublié » en recrée un), pour que ce tiers ne
                // garde pas l'accès au compte désormais vérifié.
                if (!user.IsEmailVerified)
                {
                    user.PasswordHash = null;
                    user.IsEmailVerified = true;
                    user.VerifiedAt = DateTime.UtcNow;
                }
            }
            else
            {
                // Personne inconnue : le rôle est demandé avant de créer le compte.
                if (string.IsNullOrWhiteSpace(role) || !AllowedRoles.Contains(role))
                    return new GoogleSignInOutcome(null, NeedsRole: true, IdToken: idToken,
                        Email: email, FirstName: identity.GivenName);
                if (_platformSettings != null && !_platformSettings.GetBool(PlatformSettingDefinitions.SignupEnabled))
                    return Fail("signup_disabled");

                user = new User
                {
                    Email = email,
                    PasswordHash = null,
                    FirstName = identity.GivenName ?? "",
                    LastName = identity.FamilyName ?? "",
                    IsActive = true,
                    IsEmailVerified = true,
                    VerifiedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    Role = role.ToLowerInvariant(),
                    Locale = AiLocaleHeaderHandler.Normalize(locale),
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Compte créé avec Google : {UserId} ({Role})", user.Id, user.Role);
            }

            _db.OAuthAccounts.Add(new OAuthAccount
            {
                UserId = user.Id,
                Provider = Provider,
                ProviderUserId = identity.Subject,
                Email = identity.Email,
                DisplayName = identity.Name,
                ProfileImageUrl = identity.Picture,
                ConnectedAt = DateTime.UtcNow,
                IsActive = true,
            });
            await _db.SaveChangesAsync(ct);
        }

        // 4. Session.
        var session = await _auth.SignInVerifiedUserAsync(user, request);
        return session.Success ? new GoogleSignInOutcome(session) : Fail(session.ErrorCode ?? "google_signin_failed");
    }

    private static GoogleSignInOutcome Fail(string code) => new(null, ErrorCode: code);

    private sealed record TokenResponse([property: JsonPropertyName("id_token")] string? IdToken);

    private async Task<string?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient();
            using var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = ClientId!,
                ["client_secret"] = _clientSecret!,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
            }), ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Échange du code Google refusé : {Status}", (int)response.StatusCode);
                return null;
            }
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
            return body?.IdToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échange du code Google impossible");
            return null;
        }
    }

    private sealed record GoogleIdentity(
        string Subject, string? Email, bool EmailVerified, string? Name, string? GivenName, string? FamilyName, string? Picture);

    private async Task<GoogleIdentity?> ValidateIdTokenAsync(string idToken, CancellationToken ct)
    {
        try
        {
            var oidc = await GoogleOidc.GetConfigurationAsync(ct);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(idToken, new TokenValidationParameters
            {
                ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
                ValidAudience = ClientId,
                IssuerSigningKeys = oidc.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            }, out _);
            string? Claim(string type) => principal.FindFirst(type)?.Value;
            var sub = Claim("sub");
            if (string.IsNullOrEmpty(sub)) return null;
            return new GoogleIdentity(
                sub,
                Claim("email"),
                string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase),
                Claim("name"),
                Claim("given_name"),
                Claim("family_name"),
                Claim("picture"));
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            _logger.LogWarning("Jeton d'identité Google refusé : {Reason}", ex.Message);
            return null;
        }
    }
}
