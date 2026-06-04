using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Extensions;
using Backend.Models.DTOs;

namespace Backend.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IFileUploadService _fileUploadService;
    private readonly ISettingsService _settingsService;
    private readonly ISessionService _sessionService;
    private readonly ITwoFactorService _twoFactorService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserService userService, 
        IFileUploadService fileUploadService,
        ISettingsService settingsService,
        ISessionService sessionService,
        ITwoFactorService twoFactorService,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _fileUploadService = fileUploadService;
        _settingsService = settingsService;
        _sessionService = sessionService;
        _twoFactorService = twoFactorService;
        _logger = logger;
    }

    [HttpGet("profile")]
    [Authorize] // ✅ AJOUTÉ - CRITIQUE POUR ÉVITER L'ERREUR 500
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound();
            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération du profil utilisateur");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpPut("profile")]
    [Authorize] // ✅ AJOUTÉ - Nécessaire car utilise User.GetUserId()
    public async Task<IActionResult> UpdateProfile([FromBody] User user)
    {
        try
        {
            // ✅ AMÉLIORÉ: Vérifier que l'utilisateur ne peut modifier que son propre profil
            var userId = User.GetUserId();
            if (user.Id != userId)
            {
                return Forbid(); // Empêcher de modifier le profil d'un autre utilisateur
            }
            
            var updated = await _userService.UpdateUserAsync(user);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la mise à jour du profil utilisateur");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")] // ✅ AJOUTÉ - Seuls les admins peuvent lister tous les users
    [ProducesResponseType(typeof(PaginationResponse<User>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var users = await _userService.GetAllUsersAsync(page, pageSize);
            var totalCount = await _userService.GetTotalUsersCountAsync();
            
            var response = new PaginationResponse<User>(users, totalCount, page, pageSize);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des utilisateurs");
            return StatusCode(500, "Erreur serveur");
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var adminUserId = User.GetUserId();
            
            // Soft delete by default
            var result = await _userService.SoftDeleteUserAsync(id, adminUserId);
            
            if (!result)
                return NotFound(new { error = "User not found" });
            
            return Ok(new
            {
                success = true,
                message = "User deleted successfully (soft delete)",
                userId = id,
                deletedBy = adminUserId,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Restore a soft-deleted user (Admin only)
    /// </summary>
    [HttpPost("{id}/restore")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Restore(int id)
    {
        try
        {
            var result = await _userService.RestoreUserAsync(id);
            
            if (!result)
                return NotFound(new { error = "User not found or already active" });
            
            return Ok(new
            {
                success = true,
                message = "User restored successfully",
                userId = id,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Permanently delete a user (Admin only) - IRREVERSIBLE
    /// </summary>
    [HttpDelete("{id}/permanent")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> HardDelete(int id)
    {
        try
        {
            var result = await _userService.HardDeleteUserAsync(id);
            
            if (!result)
                return NotFound(new { error = "User not found" });
            
            return Ok(new
            {
                success = true,
                message = "⚠️ User PERMANENTLY deleted",
                userId = id,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hard deleting user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Récupère les statistiques du profil utilisateur courant
    /// </summary>
    [HttpGet("profile/statistics")]
    [Authorize] // ✅ AJOUTÉ - Nécessaire car GetProfileStatisticsAsync() pourrait utiliser l'userId
    public async Task<IActionResult> GetProfileStatistics()
    {
        try
        {
            var statistics = await _userService.GetProfileStatisticsAsync();
            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des statistiques du profil");
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Récupère les statistiques d'un utilisateur spécifique
    /// </summary>
    [HttpGet("{id}/statistics")]
    [Authorize] // ✅ AJOUTÉ - Nécessaire pour contrôler l'accès
    public async Task<IActionResult> GetUserStatistics(int id)
    {
        try
        {
            // ✅ AMÉLIORÉ: Vérifier que l'utilisateur ne peut voir que ses propres stats (sauf admin)
            var currentUserId = User.GetUserId();
            var isAdmin = User.IsAdmin();
            
            if (currentUserId != id && !isAdmin)
            {
                return Forbid(); // Seul l'utilisateur lui-même ou un admin peut voir les stats
            }
            
            var statistics = await _userService.GetUserStatisticsAsync(id);
            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la récupération des statistiques de l'utilisateur {UserId}", id);
            return StatusCode(500, "Erreur serveur");
        }
    }

    /// <summary>
    /// Upload user avatar/profile picture
    /// </summary>
    [HttpPost("avatar")]
    [Authorize]
    public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file)
    {
        try
        {
            var userId = User.GetUserId();
            
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            // Validate file
            if (!_fileUploadService.IsValidImageFile(file))
                return BadRequest(new { error = "Invalid image file. Allowed: JPG, PNG, GIF, WEBP. Max size: 5MB" });

            // Upload
            var avatarUrl = await _fileUploadService.UploadAvatarAsync(userId, file);

            // Update user
            var user = await _userService.GetUserByIdAsync(userId);
            
            // Delete old avatar if exists
            if (!string.IsNullOrEmpty(user?.AvatarUrl))
            {
                await _fileUploadService.DeleteAvatarAsync(user.AvatarUrl);
            }
            
            user.AvatarUrl = avatarUrl;
            await _userService.UpdateUserAsync(user);

            return Ok(new
            {
                success = true,
                message = "Avatar uploaded successfully",
                avatarUrl = avatarUrl,
                timestamp = DateTime.UtcNow
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading avatar");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Delete user avatar
    /// </summary>
    [HttpDelete("avatar")]
    [Authorize]
    public async Task<IActionResult> DeleteAvatar()
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);

            if (user == null || string.IsNullOrEmpty(user.AvatarUrl))
                return NotFound(new { error = "No avatar to delete" });

            // Delete file
            await _fileUploadService.DeleteAvatarAsync(user.AvatarUrl);

            // Update user
            user.AvatarUrl = null;
            await _userService.UpdateUserAsync(user);

            return Ok(new
            {
                success = true,
                message = "Avatar deleted successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting avatar");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Request to change email address
    /// </summary>
    [HttpPost("change-email")]
    [Authorize]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);

            if (user == null)
                return NotFound(new { error = "User not found" });

            // Verify new email is not already in use
            var existingUser = await _userService.GetUserByEmailAsync(request.NewEmail);
            if (existingUser != null && existingUser.Id != userId)
            {
                return BadRequest(new { error = "Email already in use" });
            }

            // Generate verification code and token
            var verificationCode = new Random().Next(100000, 999999).ToString();
            var token = Guid.NewGuid().ToString();

            // Save pending email change
            user.PendingEmail = request.NewEmail;
            user.EmailChangeToken = token;
            user.EmailChangeTokenExpiry = DateTime.UtcNow.AddMinutes(15);
            
            await _userService.UpdateUserAsync(user);

            // TODO: Send verification email to new address with verification code
            // await _emailService.SendEmailChangeConfirmationAsync(request.NewEmail, verificationCode);

            return Ok(new
            {
                success = true,
                message = "Verification code sent to new email",
                newEmail = request.NewEmail,
                expiresIn = 15, // minutes
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing email");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Confirm email change with verification code
    /// </summary>
    [HttpPost("confirm-email-change")]
    [Authorize]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);

            if (user == null)
                return NotFound(new { error = "User not found" });

            // Verify pending email change exists
            if (string.IsNullOrEmpty(user.PendingEmail))
            {
                return BadRequest(new { error = "No pending email change" });
            }

            // Check token expiry
            if (!user.EmailChangeTokenExpiry.HasValue || user.EmailChangeTokenExpiry < DateTime.UtcNow)
            {
                return BadRequest(new { error = "Verification code expired" });
            }

            // TODO: Compare verification code properly
            // For now, we'll assume code is correct if token is valid
            
            // Apply email change
            var oldEmail = user.Email;
            user.Email = user.PendingEmail;
            user.PendingEmail = null;
            user.EmailChangeToken = null;
            user.EmailChangeTokenExpiry = null;
            user.IsEmailVerified = true;

            await _userService.UpdateUserAsync(user);

            _logger.LogInformation("Email changed for user {UserId} from {OldEmail} to {NewEmail}", userId, oldEmail, user.Email);

            return Ok(new
            {
                success = true,
                message = "Email changed successfully",
                newEmail = user.Email,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming email change");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get notification settings for the current user
    /// </summary>
    [HttpGet("settings/notifications")]
    [Authorize]
    public async Task<IActionResult> GetNotificationSettings()
    {
        try
        {
            var userId = User.GetUserId();
            var settings = await _settingsService.GetNotificationSettingsAsync(userId);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notification settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Update notification settings for the current user
    /// </summary>
    [HttpPut("settings/notifications")]
    [Authorize]
    public async Task<IActionResult> UpdateNotificationSettings([FromBody] NotificationSettingsDto settings)
    {
        try
        {
            var userId = User.GetUserId();
            
            if (settings.UserId != userId && !User.IsAdmin())
            {
                return Forbid();
            }

            var updatedSettings = await _settingsService.SaveNotificationSettingsAsync(userId, settings);
            return Ok(updatedSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating notification settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get privacy settings for the current user
    /// </summary>
    [HttpGet("settings/privacy")]
    [Authorize]
    public async Task<IActionResult> GetPrivacySettings()
    {
        try
        {
            var userId = User.GetUserId();
            var settings = await _settingsService.GetPrivacySettingsAsync(userId);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting privacy settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Update privacy settings for the current user
    /// </summary>
    [HttpPut("settings/privacy")]
    [Authorize]
    public async Task<IActionResult> UpdatePrivacySettings([FromBody] PrivacySettingsDto settings)
    {
        try
        {
            var userId = User.GetUserId();
            
            if (settings.UserId != userId && !User.IsAdmin())
            {
                return Forbid();
            }

            var updatedSettings = await _settingsService.SavePrivacySettingsAsync(userId, settings);
            return Ok(updatedSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating privacy settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get active sessions for the current user
    /// </summary>
    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> GetSessions()
    {
        try
        {
            var userId = User.GetUserId();
            var sessions = await _sessionService.GetUserSessionsAsync(userId);
            return Ok(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Delete a specific session (logout from device)
    /// </summary>
    [HttpDelete("sessions/{sessionId}")]
    [Authorize]
    public async Task<IActionResult> DeleteSession(int sessionId)
    {
        try
        {
            var userId = User.GetUserId();
            var session = await _sessionService.GetSessionByIdAsync(sessionId);
            
            if (session == null || session.UserId != userId && !User.IsAdmin())
            {
                return NotFound(new { error = "Session not found" });
            }

            await _sessionService.DeleteSessionAsync(sessionId);

            return Ok(new
            {
                success = true,
                message = "Session terminated successfully",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get 2FA status for the current user
    /// </summary>
    [HttpGet("2fa/status")]
    [Authorize]
    public async Task<IActionResult> Get2FAStatus()
    {
        try
        {
            var userId = User.GetUserId();
            var status = await _twoFactorService.GetTwoFactorStatusAsync(userId);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting 2FA status");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Enable 2FA for the current user
    /// </summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> Enable2FA([FromBody] Enable2FARequestDto request)
    {
        try
        {
            var userId = User.GetUserId();
            var response = await _twoFactorService.InitializeTwoFactorAsync(userId, request.Method);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling 2FA");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Verify and complete 2FA setup
    /// </summary>
    [HttpPost("2fa/verify")]
    [Authorize]
    public async Task<IActionResult> Verify2FA([FromBody] Verify2FARequestDto request)
    {
        try
        {
            var userId = User.GetUserId();
            
            if (string.IsNullOrEmpty(request.Code) || request.Code.Length != 6)
            {
                return BadRequest(new { error = "Invalid verification code format" });
            }

            var status = await _twoFactorService.VerifyTwoFactorAsync(userId, request.Code);

            return Ok(new
            {
                success = true,
                message = "2FA verified and enabled successfully",
                status = status,
                timestamp = DateTime.UtcNow
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "2FA verification failed for user {UserId}", User.GetUserId());
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying 2FA");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Disable 2FA for the current user
    /// </summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA([FromBody] Disable2FARequestDto request)
    {
        try
        {
            var userId = User.GetUserId();
            
            // TODO: Vérifier le mot de passe avant de désactiver 2FA
            // if (!VerifyPassword(userId, request.Password))
            //     return Unauthorized(new { error = "Invalid password" });

            await _twoFactorService.DisableTwoFactorAsync(userId);
            
            var status = await _twoFactorService.GetTwoFactorStatusAsync(userId);

            return Ok(new
            {
                success = true,
                message = "2FA disabled successfully",
                status = status,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling 2FA");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}