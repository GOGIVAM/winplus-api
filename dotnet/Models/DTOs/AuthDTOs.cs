namespace Backend.Models.DTOs;

public class SignUpRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
    /// <summary>Lot 10.3 : source d'acquisition captée par le web (facultative, selon le consentement).</summary>
    public Backend.Services.Marketing.AttributionDto? Attribution { get; set; }
    /// <summary>Lot 10.6 : langue du site au moment de l'inscription ("fr" ou "en", facultative).</summary>
    public string? Locale { get; set; }
}

public class SignUpResponse
{
    public string Message { get; set; } = string.Empty;
    public object? User { get; set; }
}

public class SignInRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;
    public string? DeviceId { get; set; } // ✅ For merging anonymous cart
    /// <summary>Site web : le jeton de renouvellement part en cookie HttpOnly, jamais dans la réponse.</summary>
    public bool UseCookie { get; set; }
}

/// <summary>Connexion avec Google : code (premier passage) ou jeton d'identité et rôle (second passage).</summary>
public class GoogleSignInRequestDto
{
    public string? Code { get; set; }
    public string? RedirectUri { get; set; }
    public string? IdToken { get; set; }
    public string? Role { get; set; }
    public string? Locale { get; set; }
    public string? DeviceId { get; set; }
    public Backend.Services.Marketing.AttributionDto? Attribution { get; set; }
    /// <summary>« Se souvenir de moi » : session gardée 30 jours, sinon jusqu'à la fermeture du navigateur.</summary>
    public bool RememberMe { get; set; }
    /// <summary>Site web : le jeton de renouvellement part en cookie HttpOnly, jamais dans la réponse.</summary>
    public bool UseCookie { get; set; }
}

public class SignInResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; } = "Bearer";
    public object? User { get; set; }
}

/// <summary>Second facteur de la connexion : jeton reçu à la connexion et code de l'application ou de secours.</summary>
public class VerifyTwoFactorLoginRequestDto
{
    public string TwoFactorToken { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
}

public class VerifyEmailRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string VerificationCode { get; set; } = string.Empty;
    /// <summary>Site web : le jeton de renouvellement part en cookie HttpOnly, jamais dans la réponse.</summary>
    public bool UseCookie { get; set; }
}

public class VerifyEmailResponse
{
    public string Message { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public UserDto? User { get; set; }
}

public class ResendVerificationRequestDto
{
    public string Email { get; set; } = string.Empty;
}

public class ForgotPasswordRequestDto
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequestDto
{
    public string ResetToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    /// <summary>Confirmation revérifiée côté serveur (optionnelle pour les clients anciens).</summary>
    public string? ConfirmPassword { get; set; }
}

/// <summary>Validation d'un lien de réinitialisation sans le consommer.</summary>
public class VerifyResetTokenRequestDto
{
    public string ResetToken { get; set; } = string.Empty;
}

public class RefreshTokenRequestDto
{
    /// <summary>Application mobile (et anciennes sessions web) ; le site envoie le cookie à la place.</summary>
    public string? RefreshToken { get; set; }
    /// <summary>Site web : le jeton de renouvellement part en cookie HttpOnly, jamais dans la réponse.</summary>
    public bool UseCookie { get; set; }
}

public class RefreshTokenResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; } = "Bearer";
}

public class LogoutRequestDto
{
    public string? RefreshToken { get; set; }
    /// <summary>Site web : le jeton de renouvellement part en cookie HttpOnly, jamais dans la réponse.</summary>
    public bool UseCookie { get; set; }
}

public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CurrentUserResponse
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
}

// ==================== RECONFIRMATION PÉRIODIQUE MOBILE ====================

/// <summary>Demande d'envoi du code de reconfirmation périodique (nécessite JWT valide)</summary>
public class SendConfirmationCodeRequestDto
{
    // Vide intentionnellement : l'userId est extrait du JWT
}

/// <summary>Vérification du code de reconfirmation reçu par email</summary>
public class VerifyConfirmationCodeRequestDto
{
    public string Code { get; set; } = string.Empty;
    /// <summary>Site web : le jeton de renouvellement part en cookie HttpOnly, jamais dans la réponse.</summary>
    public bool UseCookie { get; set; }
}

public class VerifyConfirmationCodeResponse
{
    public string Message { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; } = "Bearer";
    public DateTime ConfirmedAt { get; set; }
}

// Legacy DTOs for backward compatibility
public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ConfirmForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
// ==================== AUTH SERVICE RESPONSE DTOS ====================

public class AuthResult
{
    public bool Success { get; set; }
    /// <summary>Mot de passe (ou Google) accepté, mais le compte a la double authentification : aucun jeton n'est
    /// émis tant que le second code n'est pas vérifié (POST /api/auth/2fa/verify-login).</summary>
    public bool RequiresTwoFactor { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; } // ✅ Code d'erreur pour meilleurs messages frontend
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? IdToken { get; set; }
    public int? ExpiresIn { get; set; }
    public UserDto? User { get; set; }
    public Dictionary<string, string> Errors { get; set; } = new();
}

public class UserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Role { get; set; } = "student";
    public string Locale { get; set; } = "fr";
    public bool IsEmailVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
}