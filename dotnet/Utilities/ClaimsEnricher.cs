using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Utilities;

/// <summary>
/// Service pour enrichir les ClaimsPrincipal avec données PostgreSQL
/// Ajoute user_id, role depuis la BD basé sur cognito_id
/// </summary>
public interface IClaimsEnricher
{
    Task<ClaimsPrincipal> EnrichClaimsAsync(ClaimsPrincipal principal);
}

public class ClaimsEnricher : IClaimsEnricher
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ClaimsEnricher> _logger;

    public ClaimsEnricher(ApplicationDbContext dbContext, ILogger<ClaimsEnricher> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Enrichir les claims avec les données de l'utilisateur depuis PostgreSQL
    /// </summary>
    public async Task<ClaimsPrincipal> EnrichClaimsAsync(ClaimsPrincipal principal)
    {
        try
        {
            // Extraire le cognito_id (sub claim)
            var cognitoId = principal.FindFirst("sub")?.Value;
            
            if (string.IsNullOrEmpty(cognitoId))
            {
                _logger.LogWarning("No 'sub' claim found in principal");
                return principal;
            }

            // Chercher l'utilisateur dans PostgreSQL
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.CognitoId == cognitoId);

            if (user == null)
            {
                _logger.LogWarning("User not found in PostgreSQL for cognito_id: {CognitoId}", cognitoId);
                return principal;
            }

            // Créer les claims enrichis
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

