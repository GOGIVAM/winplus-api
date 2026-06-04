namespace Backend.Models.DTOs;

public class SignUpRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
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
}

public class SignInResponse
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public string? TokenType { get; set; } = "Bearer";
    public object? User { get; set; }
}

public class VerifyEmailRequestDto
{
    public int UserId { get; set; }
    public string VerificationCode { get; set; } = string.Empty;
}

public class VerifyEmailResponse
{
    public string Message { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
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
}

public class RefreshTokenRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
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
    public bool IsEmailVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
}