namespace Backend.Models.DTOs;

/// <summary>
/// Notification settings for user preferences
/// </summary>
public class NotificationSettingsDto
{
    public int UserId { get; set; }
    public bool EmailNotifications { get; set; } = true;
    public bool PushNotifications { get; set; } = true;
    public bool CourseCommunity { get; set; } = true;
    public bool Promotions { get; set; } = false;
    public bool Newsletters { get; set; } = true;
    public bool LearningReminders { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Privacy settings for user account
/// </summary>
public class PrivacySettingsDto
{
    public int UserId { get; set; }
    public bool ProfileVisible { get; set; } = true;
    public bool ShowProgressPublic { get; set; } = false;
    public bool AllowMessages { get; set; } = true;
    public bool AllowFriends { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Session information for active user sessions
/// </summary>
public class SessionDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string? Location { get; set; }
}

/// <summary>
/// Two-Factor Authentication status
/// </summary>
public class TwoFactorStatusDto
{
    public int UserId { get; set; }
    public bool IsEnabled { get; set; }
    public string? Method { get; set; } // "email", "sms", "authenticator"
    public DateTime? EnabledAt { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public int BackupCodesCount { get; set; }
    /// <summary>Activation commencée (QR code affiché) mais pas encore confirmée par un code.</summary>
    public bool PendingSetup { get; set; }
}

/// <summary>
/// Request to enable 2FA
/// </summary>
public class Enable2FARequestDto
{
    public string Method { get; set; } = "email"; // "email", "sms", "authenticator"
    public string? PhoneNumber { get; set; }
}

/// <summary>
/// Request to verify 2FA setup
/// </summary>
public class Verify2FARequestDto
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Request to disable 2FA
/// </summary>
public class Disable2FARequestDto
{
    /// <summary>Mot de passe actuel (exigé sauf pour un compte créé avec Google).</summary>
    public string? Password { get; set; }
    /// <summary>Code de l'application ou code de secours.</summary>
    public string? Code { get; set; }
}

/// <summary>Remplacement des codes de secours : code de l'application exigé.</summary>
public class RegenerateBackupCodesRequestDto
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Response for 2FA setup
/// </summary>
public class TwoFactorSetupResponse
{
    /// <summary>QR code en image PNG (data:image/png;base64,…), généré par le serveur.</summary>
    public string? QrCode { get; set; }
    public List<string>? BackupCodes { get; set; }
    /// <summary>Clé base32 à saisir à la main si le QR code ne peut pas être scanné.</summary>
    public string? Secret { get; set; }
    /// <summary>Même clé, par groupes de 4 caractères, pour l'affichage.</summary>
    public string? SecretGrouped { get; set; }
    /// <summary>Lien otpauth:// (ouvre directement l'application d'authentification sur le même téléphone).</summary>
    public string? OtpauthUrl { get; set; }
    public string Message { get; set; } = string.Empty;
}
