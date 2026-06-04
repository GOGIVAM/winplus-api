using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Backend.Extensions;

/// <summary>
/// Extensions pour extraire les informations utilisateur des claims JWT
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Extrait l'ID utilisateur du token JWT avec multiples fallbacks
    /// </summary>
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        if (principal == null)
            throw new UnauthorizedAccessException("Principal is null");

        // Essayer plusieurs sources de claim (dans l'ordre de préférence)
        var userIdClaim = principal.FindFirst("user_id") 
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)
                       ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)
                       ?? principal.FindFirst("sub");
        
        if (userIdClaim == null)
        {
            // Logger tous les claims disponibles pour debug
            var allClaims = string.Join(", ", principal.Claims.Select(c => $"{c.Type}={c.Value}"));
            throw new UnauthorizedAccessException(
                $"Token does not contain user_id claim. Available claims: {allClaims}"
            );
        }
        
        if (!int.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException(
                $"Invalid user_id claim value: {userIdClaim.Value}. Could not parse as integer."
            );
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
            ?? principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
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
