using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Utilities;

/// <summary>
/// Utilitaire pour générer des tokens JWT
/// </summary>
public class JwtTokenGenerator
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _secretKey = configuration["Jwt:SecretKey"] ?? "your-super-secret-key-for-jwt-token-generation-12345-super-long-key";
        _issuer = configuration["Jwt:Issuer"] ?? "winplusApp";
        _audience = configuration["Jwt:Audience"] ?? "winplusUsers";
        _expirationMinutes = int.Parse(configuration["Jwt:ExpirationMinutes"] ?? "60");
    }

    public string GenerateToken(Dictionary<string, object> claims, int? expirationMinutes = null)
    {
        try
        {
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claimsList = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            };

            // Ajouter les claims fournis
            foreach (var kvp in claims)
            {
                claimsList.Add(new Claim(kvp.Key, kvp.Value?.ToString() ?? ""));
            }

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claimsList,
                expires: DateTime.UtcNow.AddMinutes(expirationMinutes ?? _expirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error generating JWT token: {ex.Message}", ex);
        }
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = System.Text.Encoding.UTF8.GetBytes(_secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _issuer,
                ValidAudience = _audience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero // Élimine le délai de grâce
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            throw new UnauthorizedAccessException("Le token a expiré. Veuillez vous reconnecter.");
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException($"Erreur lors de la validation du token: {ex.Message}");
        }
    }
}
