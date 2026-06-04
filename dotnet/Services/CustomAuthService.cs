using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Backend.Data;
using Backend.Models;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using BC = BCrypt.Net.BCrypt;

namespace Backend.Services;

/// <summary>
/// Custom Authentication Service - replaces Cognito
/// </summary>
public interface ICustomAuthService
{
    Task<AuthResult> SignUpAsync(string email, string password, string firstName, string lastName, string? phone = null);
    Task<AuthResult> SignInAsync(string email, string password, HttpRequest request, bool rememberMe = false);
    Task<AuthResult> VerifyEmailAsync(int userId, string verificationCode);
    Task<AuthResult> ResendVerificationCodeAsync(string email);
    Task<AuthResult> ForgotPasswordAsync(string email);
    Task<AuthResult> ResetPasswordAsync(string resetToken, string newPassword);
    Task<AuthResult> RefreshAccessTokenAsync(string refreshToken);
    Task<bool> LogoutAsync(string refreshToken);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}

public class CustomAuthService : ICustomAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly IEmailService _emailService;
    private readonly IDeviceTrackingService _deviceTrackingService;
    private readonly ILogger<CustomAuthService> _logger;
    private readonly int _emailVerificationExpirationHours;
    private readonly int _passwordResetExpirationHours;

    public CustomAuthService(
        ApplicationDbContext dbContext,
        IJwtService jwtService,
        IEmailService emailService,
        IDeviceTrackingService deviceTrackingService,
        IConfiguration configuration,
        ILogger<CustomAuthService> logger)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _emailService = emailService;
        _deviceTrackingService = deviceTrackingService;
        _logger = logger;
        _emailVerificationExpirationHours = int.TryParse(
            configuration["Auth:EmailVerificationExpirationHours"], out var hours) ? hours : 24;
        _passwordResetExpirationHours = int.TryParse(
            configuration["Auth:PasswordResetExpirationHours"], out var pwHours) ? pwHours : 1;
    }

    /// <summary>
    /// Sign up new user with email and password
    /// </summary>
    public async Task<AuthResult> SignUpAsync(
        string email,
        string password,
        string firstName,
        string lastName,
        string? phone = null)
    {
        try
        {
            _logger.LogInformation("SignUp request for email: {Email}", email);

            // Validation
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Email and password are required",
                    Errors = new() { { "email", "Email is required" }, { "password", "Password is required" } }
                };
            }

            if (password.Length < 8)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Password must be at least 8 characters long",
                    Errors = new() { { "password", "Password must be at least 8 characters" } }
                };
            }

            // Validate password complexity
            if (!ValidatePasswordComplexity(password))
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Password must contain uppercase, lowercase, number, and special character",
                    Errors = new() { { "password", "Invalid password complexity" } }
                };
            }

            // Check if user already exists
            var existingUser = _dbContext.Users.FirstOrDefault(u => u.Email == email);
            if (existingUser != null)
            {
                _logger.LogWarning("User already exists: {Email}", email);
                return new AuthResult
                {
                    Success = false,
                    Message = "Email is already registered",
                    Errors = new() { { "email", "Email already registered" } }
                };
            }

            // Hash password
            var passwordHash = BC.HashPassword(password);

            // Create user
            var user = new User
            {
                Email = email,
                PasswordHash = passwordHash,
                FirstName = firstName ?? "",
                LastName = lastName ?? "",
                Phone = phone,
                IsActive = true,
                IsEmailVerified = false,
                CreatedAt = DateTime.UtcNow,
                Role = "student"
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            // Generate verification code
            var verificationCode = GenerateVerificationCode();
            var emailVerificationToken = new EmailVerificationToken
            {
                UserId = user.Id,
                VerificationCode = verificationCode,
                ExpiresAt = DateTime.UtcNow.AddHours(_emailVerificationExpirationHours),
                IsVerified = false
            };

            _dbContext.EmailVerificationTokens.Add(emailVerificationToken);
            await _dbContext.SaveChangesAsync();

            // Send verification email
            await _emailService.SendEmailVerificationAsync(email, firstName ?? "", verificationCode);

            _logger.LogInformation("User created successfully: {Email}", email);

            return new AuthResult
            {
                Success = true,
                Message = "Registration successful. Please check your email to verify your account.",
                User = new UserDto
                {
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    IsEmailVerified = false
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SignUp for email: {Email}", email);
            return new AuthResult
            {
                Success = false,
                Message = "An error occurred during registration",
                Errors = new() { { "signup", ex.Message } }
            };
        }
    }

    /// <summary>
    /// Sign in user with email and password
    /// </summary>
    public async Task<AuthResult> SignInAsync(string email, string password, HttpRequest request, bool rememberMe = false)
    {
        try
        {
            _logger.LogInformation("SignIn request for email: {Email}", email);

            // Validation
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Email and password are required",
                    Errors = new() { { "signin", "Invalid credentials" } }
                };
            }

            // Get user
            var user = _dbContext.Users.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                _logger.LogWarning("User not found: {Email}", email);
                return new AuthResult
                {
                    Success = false,
                    Message = "Invalid email or password",
                    Errors = new() { { "signin", "Invalid credentials" } }
                };
            }

            // Check if email is verified
            if (!user.IsEmailVerified)
            {
                _logger.LogWarning("Login attempt with unverified email: {Email}", email);
                return new AuthResult
                {
                    Success = false,
                    Message = "Please verify your email before logging in. Check your inbox for the verification code.",
                    ErrorCode = "EMAIL_NOT_VERIFIED", // ✅ Code d'erreur spécifique
                    Errors = new() { { "email_verification", "Email not verified" } }
                };
            }

            // Verify password
            if (user.PasswordHash == null || !BC.Verify(password, user.PasswordHash))
            {
                _logger.LogWarning("Invalid password for user: {Email}", email);
                return new AuthResult
                {
                    Success = false,
                    Message = "Invalid email or password",
                    Errors = new() { { "signin", "Invalid credentials" } }
                };
            }

            if (!user.IsActive)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Account is inactive",
                    Errors = new() { { "account", "Account is inactive" } }
                };
            }

            // Track device and check if it's recognized
            var device = await _deviceTrackingService.TrackDeviceAsync(user.Id, request, rememberMe);

            // Generate tokens
            var accessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Role);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Save refresh token
            var refreshTokenEntity = new RefreshToken
            {
                UserId = user.Id,
                Token = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.RefreshTokens.Add(refreshTokenEntity);

            // Update last login
            user.LastLoginAt = DateTime.UtcNow;
            _dbContext.Update(user);

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("SignIn successful for user: {Email}", email);

            return new AuthResult
            {
                Success = true,
                Message = "Login successful",
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 900, // 15 minutes
                User = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Role = user.Role,
                    IsEmailVerified = true,
                    VerifiedAt = user.VerifiedAt
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SignIn for email: {Email}", email);
            return new AuthResult
            {
                Success = false,
                Message = "An error occurred during login",
                Errors = new() { { "signin", ex.Message } }
            };
        }
    }

    /// <summary>
    /// Verify email with verification code
    /// </summary>
    public async Task<AuthResult> VerifyEmailAsync(int userId, string verificationCode)
    {
        try
        {
            _logger.LogInformation("Verifying email for user: {UserId}", userId);

            // Get user
            var user = _dbContext.Users.Find(userId);
            if (user == null)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "User not found",
                    Errors = new() { { "user", "User not found" } }
                };
            }

            // Get most recent verification token
            var token = _dbContext.EmailVerificationTokens
                .Where(t => t.UserId == userId && !t.IsVerified)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();

            if (token == null)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "No verification code found",
                    Errors = new() { { "verification_code", "No verification code found" } }
                };
            }

            // Check expiration
            if (token.IsExpired)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Verification code has expired",
                    Errors = new() { { "verification_code", "Code expired" } }
                };
            }

            // Check attempts
            if (token.IsBlocked)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Too many failed attempts. Please request a new code.",
                    Errors = new() { { "verification_code", "Too many attempts" } }
                };
            }

            // Verify code
            if (token.VerificationCode != verificationCode)
            {
                token.AttemptCount++;
                _dbContext.Update(token);
                await _dbContext.SaveChangesAsync();

                return new AuthResult
                {
                    Success = false,
                    Message = "Invalid verification code",
                    Errors = new() { { "verification_code", "Invalid code" } }
                };
            }

            // Mark as verified
            user.IsEmailVerified = true;
            user.VerifiedAt = DateTime.UtcNow;
            token.IsVerified = true;
            token.VerifiedAt = DateTime.UtcNow;

            _dbContext.Update(user);
            _dbContext.Update(token);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Email verified for user: {UserId}", userId);

            return new AuthResult
            {
                Success = true,
                Message = "Email verified successfully",
                User = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    IsEmailVerified = true
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email for user: {UserId}", userId);
            return new AuthResult
            {
                Success = false,
                Message = "An error occurred during email verification",
                Errors = new() { { "verification", ex.Message } }
            };
        }
    }

    /// <summary>
    /// Resend verification code
    /// </summary>
    public async Task<AuthResult> ResendVerificationCodeAsync(string email)
    {
        try
        {
            _logger.LogInformation("Resending verification code for email: {Email}", email);

            var user = _dbContext.Users.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "User not found",
                    Errors = new() { { "email", "User not found" } }
                };
            }

            // Delete old verification tokens
            var oldTokens = _dbContext.EmailVerificationTokens
                .Where(t => t.UserId == user.Id && !t.IsVerified)
                .ToList();

            _dbContext.RemoveRange(oldTokens);

            // Generate new code
            var verificationCode = GenerateVerificationCode();
            var emailVerificationToken = new EmailVerificationToken
            {
                UserId = user.Id,
                VerificationCode = verificationCode,
                ExpiresAt = DateTime.UtcNow.AddHours(_emailVerificationExpirationHours),
                IsVerified = false
            };

            _dbContext.EmailVerificationTokens.Add(emailVerificationToken);
            await _dbContext.SaveChangesAsync();

            // Send email
            await _emailService.SendEmailVerificationAsync(email, user.FirstName ?? "", verificationCode);

            _logger.LogInformation("Verification code resent for email: {Email}", email);

            return new AuthResult
            {
                Success = true,
                Message = "Verification code sent to your email"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification code for email: {Email}", email);
            return new AuthResult
            {
                Success = false,
                Message = "An error occurred",
                Errors = new() { { "resend", ex.Message } }
            };
        }
    }

    /// <summary>
    /// Forgot password - send reset link
    /// </summary>
    public async Task<AuthResult> ForgotPasswordAsync(string email)
    {
        try
        {
            _logger.LogInformation("Forgot password request for email: {Email}", email);

            var user = _dbContext.Users.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                // Don't reveal if email exists
                return new AuthResult
                {
                    Success = true,
                    Message = "If email exists, you will receive a password reset link"
                };
            }

            // Delete old reset tokens
            var oldTokens = _dbContext.PasswordResetTokens
                .Where(t => t.UserId == user.Id && !t.IsUsed)
                .ToList();

            _dbContext.RemoveRange(oldTokens);

            // Generate reset token
            var resetToken = _jwtService.GeneratePasswordResetToken();
            var passwordResetToken = new PasswordResetToken
            {
                UserId = user.Id,
                Token = resetToken,
                ExpiresAt = DateTime.UtcNow.AddHours(_passwordResetExpirationHours),
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PasswordResetTokens.Add(passwordResetToken);
            await _dbContext.SaveChangesAsync();

            // Send email
            await _emailService.SendPasswordResetAsync(email, user.FirstName ?? "", resetToken);

            _logger.LogInformation("Password reset email sent for user: {Email}", email);

            return new AuthResult
            {
                Success = true,
                Message = "If email exists, you will receive a password reset link"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during forgot password for email: {Email}", email);
            return new AuthResult
            {
                Success = false,
                Message = "An error occurred",
                Errors = new() { { "forgot_password", ex.Message } }
            };
        }
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    public async Task<AuthResult> ResetPasswordAsync(string resetToken, string newPassword)
    {
        try
        {
            _logger.LogInformation("Reset password request");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Password must be at least 8 characters",
                    Errors = new() { { "password", "Invalid password" } }
                };
            }

            // Validate token
            var (isValid, _) = _jwtService.ValidatePasswordResetToken(resetToken);
            if (!isValid)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Invalid reset token",
                    Errors = new() { { "token", "Invalid token" } }
                };
            }

            // Get reset token from DB
            var passwordResetToken = _dbContext.PasswordResetTokens
                .FirstOrDefault(t => t.Token == resetToken && !t.IsUsed);

            if (passwordResetToken == null || passwordResetToken.IsExpired)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Reset token has expired",
                    Errors = new() { { "token", "Token expired" } }
                };
            }

            var user = _dbContext.Users.Find(passwordResetToken.UserId);
            if (user == null)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "User not found",
                    Errors = new() { { "user", "User not found" } }
                };
            }

            // Hash new password
            var passwordHash = BC.HashPassword(newPassword);
            user.PasswordHash = passwordHash;

            // Mark token as used and invalidate all refresh tokens
            passwordResetToken.IsUsed = true;
            passwordResetToken.UsedAt = DateTime.UtcNow;

            var refreshTokens = _dbContext.RefreshTokens
                .Where(t => t.UserId == user.Id && !t.IsRevoked)
                .ToList();

            foreach (var token in refreshTokens)
            {
                token.RevokedAt = DateTime.UtcNow;
            }

            _dbContext.Update(user);
            _dbContext.Update(passwordResetToken);
            _dbContext.UpdateRange(refreshTokens);
            await _dbContext.SaveChangesAsync();

            // Send confirmation email
            await _emailService.SendPasswordChangedAsync(user.Email, user.FirstName ?? "");

            _logger.LogInformation("Password reset successful for user: {UserId}", user.Id);

            return new AuthResult
            {
                Success = true,
                Message = "Password reset successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password reset");
            return new AuthResult
            {
                Success = false,
                Message = "An error occurred",
                Errors = new() { { "reset", ex.Message } }
            };
        }
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    public async Task<AuthResult> RefreshAccessTokenAsync(string refreshToken)
    {
        try
        {
            _logger.LogInformation("Refreshing access token");

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Refresh token is required",
                    Errors = new() { { "refresh_token", "Token required" } }
                };
            }

            // Get refresh token from DB
            var refreshTokenEntity = _dbContext.RefreshTokens
                .FirstOrDefault(t => t.Token == refreshToken);

            if (refreshTokenEntity == null)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Invalid refresh token",
                    Errors = new() { { "refresh_token", "Invalid token" } }
                };
            }

            if (!refreshTokenEntity.IsValid)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "Refresh token has expired or been revoked",
                    Errors = new() { { "refresh_token", "Token expired" } }
                };
            }

            // Get user
            var user = _dbContext.Users.Find(refreshTokenEntity.UserId);
            if (user == null || !user.IsActive)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = "User not found or inactive",
                    Errors = new() { { "user", "Invalid user" } }
                };
            }

            // Generate new tokens
            var newAccessToken = _jwtService.GenerateAccessToken(user.Id, user.Email, user.Role);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            // Save new refresh token
            var newRefreshTokenEntity = new RefreshToken
            {
                UserId = user.Id,
                Token = newRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };

            // Revoke old refresh token
            refreshTokenEntity.RevokedAt = DateTime.UtcNow;

            _dbContext.Update(refreshTokenEntity);
            _dbContext.Add(newRefreshTokenEntity);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Access token refreshed for user: {UserId}", user.Id);

            return new AuthResult
            {
                Success = true,
                Message = "Token refreshed successfully",
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresIn = 900
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing access token");
            return new AuthResult
            {
                Success = false,
                Message = "An error occurred",
                Errors = new() { { "refresh", ex.Message } }
            };
        }
    }

    /// <summary>
    /// Logout - revoke refresh token
    /// </summary>
    public async Task<bool> LogoutAsync(string refreshToken)
    {
        try
        {
            _logger.LogInformation("Logout request");

            if (string.IsNullOrWhiteSpace(refreshToken))
                return true;

            var refreshTokenEntity = _dbContext.RefreshTokens
                .FirstOrDefault(t => t.Token == refreshToken);

            if (refreshTokenEntity != null && !refreshTokenEntity.IsRevoked)
            {
                refreshTokenEntity.RevokedAt = DateTime.UtcNow;
                _dbContext.Update(refreshTokenEntity);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Logout successful for user: {UserId}", refreshTokenEntity.UserId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return false;
        }
    }

    /// <summary>
    /// Change password
    /// </summary>
    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        try
        {
            _logger.LogInformation("Change password request for user: {UserId}", userId);

            var user = _dbContext.Users.Find(userId);
            if (user == null)
                return false;

            // Verify current password
            if (user.PasswordHash == null || !BC.Verify(currentPassword, user.PasswordHash))
                return false;

            // Validate new password
            if (newPassword.Length < 8 || !ValidatePasswordComplexity(newPassword))
                return false;

            // Hash and save new password
            user.PasswordHash = BC.HashPassword(newPassword);
            _dbContext.Update(user);

            // Revoke all refresh tokens
            var refreshTokens = _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && !t.IsRevoked)
                .ToList();

            foreach (var token in refreshTokens)
            {
                token.RevokedAt = DateTime.UtcNow;
            }

            _dbContext.UpdateRange(refreshTokens);
            await _dbContext.SaveChangesAsync();

            // Send confirmation email
            await _emailService.SendPasswordChangedAsync(user.Email, user.FirstName ?? "");

            _logger.LogInformation("Password changed for user: {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user: {UserId}", userId);
            return false;
        }
    }

    // ============ Private Helper Methods ============

    private string GenerateVerificationCode()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }

    private bool ValidatePasswordComplexity(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(ch => !char.IsLetterOrDigit(ch));

        return hasUpper && hasLower && hasDigit && hasSpecial;
    }
}
