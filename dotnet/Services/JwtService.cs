using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Services;

/// <summary>
/// JWT Token generation and validation service
/// </summary>
public interface IJwtService
{
    string GenerateAccessToken(int userId, string email, string role = "student", IDictionary<string, object>? claims = null);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
    (bool isValid, ClaimsPrincipal? principal) ValidateRefreshToken(string token);
    string GeneratePasswordResetToken();
    (bool isValid, string? email) ValidatePasswordResetToken(string token);
}

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<JwtService> _logger;
    private readonly SecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenExpirationMinutes;

    public JwtService(IConfiguration configuration, ILogger<JwtService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var secretKey = configuration["JWT:SecretKey"] 
            ?? throw new InvalidOperationException("JWT:SecretKey not configured");
        var issuer = configuration["JWT:Issuer"] ?? "WinPlusApp";
        var audience = configuration["JWT:Audience"] ?? "WinPlusUsers";
        var expirationMinutes = int.TryParse(configuration["JWT:AccessTokenExpirationMinutes"], out var expiration) 
            ? expiration 
            : 15;

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        _issuer = issuer;
        _audience = audience;
        _accessTokenExpirationMinutes = expirationMinutes;

        _logger.LogInformation("JwtService initialized with issuer: {Issuer}, audience: {Audience}, expiration: {Expiration} minutes",
            _issuer, _audience, _accessTokenExpirationMinutes);
    }

    /// <summary>
    /// Generate an access token (JWT)
    /// </summary>
    public string GenerateAccessToken(
        int userId, 
        string email, 
        string role = "student", 
        IDictionary<string, object>? additionalClaims = null)
    {
        try
        {
            var claims = new List<Claim>
            {
                new("user_id", userId.ToString()),
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Email, email),
                new(ClaimTypes.Role, role),
                new("sub", userId.ToString()), // For OAuth compatibility
                new("email_verified", "true")
            };

            // Add additional claims
            if (additionalClaims != null)
            {
                foreach (var claim in additionalClaims)
                {
                    claims.Add(new Claim(claim.Key, claim.Value.ToString() ?? ""));
                }
            }

            var credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256Signature);
            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes),
                signingCredentials: credentials
            );

            var handler = new JwtSecurityTokenHandler();
            var tokenString = handler.WriteToken(token);

            _logger.LogInformation(
                "[JWT Generate] 🔑 Token d'accès généré\n" +
                "UserId: {UserId}\n" +
                "Email: {Email}\n" +
                "Role: {Role}\n" +
                "Issuer: {Issuer}\n" +
                "Audience: {Audience}\n" +
                "ExpiresIn: {ExpiresInMinutes} minutes\n" +
                "TokenLength: {TokenLength}\n" +
                "TokenPreview: {TokenPreview}\n" +
                "Timestamp: {Timestamp}",
                userId,
                email,
                role,
                _issuer,
                _audience,
                _accessTokenExpirationMinutes,
                tokenString.Length,
                tokenString.Substring(0, Math.Min(30, tokenString.Length)) + "...",
                DateTime.UtcNow
            );

            return tokenString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[JWT Generate] ❌ Erreur lors de la génération du token\n" +
                "UserId: {UserId}\n" +
                "Email: {Email}\n" +
                "Exception: {ExceptionMessage}",
                userId,
                email,
                ex.Message
            );
            throw;
        }
    }

    /// <summary>
    /// Generate a refresh token (random string)
    /// </summary>
    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    /// <summary>
    /// Validate and extract claims from JWT token
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = ClaimTypes.NameIdentifier
            }, out SecurityToken validatedToken);

            var userId = principal?.FindFirst("user_id")?.Value;
            var email = principal?.FindFirst(ClaimTypes.Email)?.Value;
            var role = principal?.FindFirst(ClaimTypes.Role)?.Value;
            var jwtToken = validatedToken as JwtSecurityToken;

            _logger.LogInformation(
                "[JWT Validate] ✅ Token validé avec succès\n" +
                "UserId: {UserId}\n" +
                "Email: {Email}\n" +
                "Role: {Role}\n" +
                "Issuer: {Issuer}\n" +
                "Audience: {Audience}\n" +
                "IssuedAt: {IssuedAt}\n" +
                "ExpiresAt: {ExpiresAt}\n" +
                "TokenPreview: {TokenPreview}",
                userId,
                email,
                role,
                jwtToken?.Issuer,
                jwtToken?.Audiences.FirstOrDefault(),
                jwtToken?.IssuedAt.ToUniversalTime(),
                jwtToken?.ValidTo.ToUniversalTime(),
                token.Substring(0, Math.Min(30, token.Length)) + "..."
            );

            return principal;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(
                "[JWT Validate] ⚠️ Token invalide\n" +
                "Exception: {Exception}\n" +
                "TokenPreview: {TokenPreview}",
                ex.Message,
                token.Substring(0, Math.Min(30, token.Length)) + "..."
            );
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[JWT Validate] ❌ Erreur lors de la validation\n" +
                "Exception: {ExceptionMessage}\n" +
                "TokenPreview: {TokenPreview}",
                ex.Message,
                token.Substring(0, Math.Min(30, token.Length)) + "..."
            );
            return null;
        }
    }

    /// <summary>
    /// Validate refresh token (just check if it follows the format)
    /// Database validation is done separately
    /// </summary>
    public (bool isValid, ClaimsPrincipal? principal) ValidateRefreshToken(string token)
    {
        try
        {
            // Refresh tokens are just random strings, so we just check if they exist and are not empty
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning(
                    "[JWT Validate RefreshToken] ❌ Refresh token vide"
                );
                return (false, null);
            }

            // Try to decode as base64 (refresh tokens are base64 encoded)
            try
            {
                Convert.FromBase64String(token);
                _logger.LogInformation(
                    "[JWT Validate RefreshToken] ✅ Refresh token valide\n" +
                    "TokenLength: {TokenLength}\n" +
                    "TokenPreview: {TokenPreview}",
                    token.Length,
                    token.Substring(0, Math.Min(20, token.Length)) + "..."
                );
                return (true, null); // Database will do further validation
            }
            catch (FormatException)
            {
                _logger.LogWarning(
                    "[JWT Validate RefreshToken] ⚠️ Format de refresh token invalide"
                );
                return (false, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[JWT Validate RefreshToken] ❌ Erreur lors de la validation\n" +
                "Exception: {ExceptionMessage}",
                ex.Message
            );
            return (false, null);
        }
    }

    /// <summary>
    /// Generate password reset token (same as refresh token)
    /// </summary>
    public string GeneratePasswordResetToken()
    {
        return GenerateRefreshToken();
    }

    /// <summary>
    /// Validate password reset token (basic validation, database validation done separately)
    /// </summary>
    public (bool isValid, string? email) ValidatePasswordResetToken(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, null);
            }

            // Try to decode as base64
            try
            {
                Convert.FromBase64String(token);
            }
            catch (FormatException)
            {
                return (false, null);
            }

            return (true, null); // Database will do further validation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating password reset token");
            return (false, null);
        }
    }
}
