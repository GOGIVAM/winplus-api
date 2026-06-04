using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;
using Backend.Models.Entities;
using Backend.Models.DTOs;
using Backend.Extensions;

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
    private readonly ApplicationDbContext _db;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserService userService,
        IFileUploadService fileUploadService,
        ISettingsService settingsService,
        ISessionService sessionService,
        ITwoFactorService twoFactorService,
        ApplicationDbContext db,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _fileUploadService = fileUploadService;
        _settingsService = settingsService;
        _sessionService = sessionService;
        _twoFactorService = twoFactorService;
        _db = db;
        _logger = logger;
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            return Ok(new ProfileResponse
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Phone = user.Phone,
                Bio = user.Bio,
                Level = user.Level,
                City = user.City,
                AvatarUrl = user.AvatarUrl,
                Role = user.Role,
                IsEmailVerified = user.IsEmailVerified,
                CreatedAt = user.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            if (request.FirstName != null) user.FirstName = request.FirstName;
            if (request.LastName != null) user.LastName = request.LastName;
            if (request.Phone != null) user.Phone = request.Phone;
            if (request.Bio != null) user.Bio = request.Bio;
            if (request.Level != null) user.Level = request.Level;
            if (request.City != null) user.City = request.City;

            var updated = await _userService.UpdateUserAsync(user);

            return Ok(new ProfileResponse
            {
                Id = updated.Id,
                Email = updated.Email,
                FirstName = updated.FirstName,
                LastName = updated.LastName,
                Phone = updated.Phone,
                Bio = updated.Bio,
                Level = updated.Level,
                City = updated.City,
                AvatarUrl = updated.AvatarUrl,
                Role = updated.Role,
                IsEmailVerified = updated.IsEmailVerified,
                CreatedAt = updated.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("profile/avatar")]
    [Authorize]
    public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file)
    {
        try
        {
            var userId = User.GetUserId();

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            if (!_fileUploadService.IsValidImageFile(file))
                return BadRequest(new { error = "Invalid image file. Allowed: JPG, PNG, GIF, WEBP. Max size: 5MB" });

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            if (!string.IsNullOrEmpty(user.AvatarUrl))
                await _fileUploadService.DeleteAvatarAsync(user.AvatarUrl);

            var avatarUrl = await _fileUploadService.UploadAvatarAsync(userId, file);
            user.AvatarUrl = avatarUrl;
            await _userService.UpdateUserAsync(user);

            return Ok(new { avatarUrl });
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

    // Legacy route — redirect to /api/users/profile/avatar
    [HttpPost("avatar")]
    [Authorize]
    public IActionResult UploadAvatarLegacy()
        => RedirectPermanentPreserveMethod("profile/avatar");

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

            await _fileUploadService.DeleteAvatarAsync(user.AvatarUrl);
            user.AvatarUrl = null;
            await _userService.UpdateUserAsync(user);

            return Ok(new { success = true, message = "Avatar deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting avatar");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("profile/statistics")]
    [Authorize]
    public async Task<IActionResult> GetProfileStatistics()
    {
        try
        {
            var userId = User.GetUserId();

            var totalCoursesEnrolled = await _db.Enrollments
                .CountAsync(e => e.UserId == userId);

            var completedCourses = await _db.Enrollments
                .CountAsync(e => e.UserId == userId && e.IsCompleted);

            var avgScore = await _db.LearningHistories
                .Where(h => h.UserId == userId && h.QuizScore != null)
                .Select(h => (double?)h.QuizScore)
                .AverageAsync() ?? 0.0;

            var totalTimeSeconds = await _db.LearningHistories
                .Where(h => h.UserId == userId && h.TimeSpentSeconds != null)
                .SumAsync(h => (int?)h.TimeSpentSeconds) ?? 0;

            var quizCompleted = await _db.LearningHistories
                .CountAsync(h => h.UserId == userId && h.ActivityType == "quiz_attempt");

            return Ok(new ProfileStatisticsResponse
            {
                TotalCoursesEnrolled = totalCoursesEnrolled,
                CompletedCourses = completedCourses,
                AverageScore = Math.Round(avgScore, 2),
                TotalTimeSeconds = totalTimeSeconds,
                QuizCompleted = quizCompleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile statistics");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("profile/subscriptions")]
    [Authorize]
    public async Task<IActionResult> GetProfileSubscriptions()
    {
        try
        {
            var userId = User.GetUserId();

            var subs = await _db.Subscriptions
                .Where(s => s.UserId == userId && !s.IsDeleted)
                .Include(s => s.PricingPlan)
                .OrderByDescending(s => s.StartDate)
                .Select(s => new ProfileSubscriptionDto
                {
                    Id = s.Id,
                    PlanName = s.PricingPlan != null ? s.PricingPlan.Name : null,
                    Price = s.PricingPlan != null ? s.PricingPlan.Price : 0,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    Status = s.Status,
                    RenewalCount = s.RenewalCount
                })
                .ToListAsync();

            return Ok(subs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile subscriptions");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
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
            _logger.LogError(ex, "Error getting all users");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var adminUserId = User.GetUserId();
            var result = await _userService.SoftDeleteUserAsync(id, adminUserId);

            if (!result) return NotFound(new { error = "User not found" });

            return Ok(new { success = true, message = "User deleted successfully", userId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("{id}/restore")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Restore(int id)
    {
        try
        {
            var result = await _userService.RestoreUserAsync(id);

            if (!result) return NotFound(new { error = "User not found or already active" });

            return Ok(new { success = true, message = "User restored successfully", userId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("{id}/permanent")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> HardDelete(int id)
    {
        try
        {
            var result = await _userService.HardDeleteUserAsync(id);

            if (!result) return NotFound(new { error = "User not found" });

            return Ok(new { success = true, message = "User permanently deleted", userId = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hard deleting user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{id}/statistics")]
    [Authorize]
    public async Task<IActionResult> GetUserStatistics(int id)
    {
        try
        {
            var currentUserId = User.GetUserId();
            if (currentUserId != id && !User.IsAdmin())
                return Forbid();

            var statistics = await _userService.GetUserStatisticsAsync(id);
            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting statistics for user {UserId}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("change-email")]
    [Authorize]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound(new { error = "User not found" });

            var existing = await _userService.GetUserByEmailAsync(request.NewEmail);
            if (existing != null && existing.Id != userId)
                return BadRequest(new { error = "Email already in use" });

            user.PendingEmail = request.NewEmail;
            user.EmailChangeToken = Guid.NewGuid().ToString();
            user.EmailChangeTokenExpiry = DateTime.UtcNow.AddMinutes(15);
            await _userService.UpdateUserAsync(user);

            return Ok(new { success = true, message = "Verification code sent to new email", newEmail = request.NewEmail, expiresIn = 15 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing email");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("confirm-email-change")]
    [Authorize]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound(new { error = "User not found" });

            if (string.IsNullOrEmpty(user.PendingEmail))
                return BadRequest(new { error = "No pending email change" });

            if (!user.EmailChangeTokenExpiry.HasValue || user.EmailChangeTokenExpiry < DateTime.UtcNow)
                return BadRequest(new { error = "Verification code expired" });

            user.Email = user.PendingEmail;
            user.PendingEmail = null;
            user.EmailChangeToken = null;
            user.EmailChangeTokenExpiry = null;
            user.IsEmailVerified = true;
            await _userService.UpdateUserAsync(user);

            return Ok(new { success = true, message = "Email changed successfully", newEmail = user.Email });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming email change");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("settings/notifications")]
    [Authorize]
    public async Task<IActionResult> GetNotificationSettings()
    {
        try
        {
            var settings = await _settingsService.GetNotificationSettingsAsync(User.GetUserId());
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notification settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("settings/notifications")]
    [Authorize]
    public async Task<IActionResult> UpdateNotificationSettings([FromBody] NotificationSettingsDto settings)
    {
        try
        {
            var userId = User.GetUserId();
            if (settings.UserId != userId && !User.IsAdmin()) return Forbid();

            var updated = await _settingsService.SaveNotificationSettingsAsync(userId, settings);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating notification settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("settings/privacy")]
    [Authorize]
    public async Task<IActionResult> GetPrivacySettings()
    {
        try
        {
            var settings = await _settingsService.GetPrivacySettingsAsync(User.GetUserId());
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting privacy settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("settings/privacy")]
    [Authorize]
    public async Task<IActionResult> UpdatePrivacySettings([FromBody] PrivacySettingsDto settings)
    {
        try
        {
            var userId = User.GetUserId();
            if (settings.UserId != userId && !User.IsAdmin()) return Forbid();

            var updated = await _settingsService.SavePrivacySettingsAsync(userId, settings);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating privacy settings");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> GetSessions()
    {
        try
        {
            var sessions = await _sessionService.GetUserSessionsAsync(User.GetUserId());
            return Ok(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("sessions/{sessionId}")]
    [Authorize]
    public async Task<IActionResult> DeleteSession(int sessionId)
    {
        try
        {
            var userId = User.GetUserId();
            var session = await _sessionService.GetSessionByIdAsync(sessionId);

            if (session == null || (session.UserId != userId && !User.IsAdmin()))
                return NotFound(new { error = "Session not found" });

            await _sessionService.DeleteSessionAsync(sessionId);
            return Ok(new { success = true, message = "Session terminated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting session");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("2fa/status")]
    [Authorize]
    public async Task<IActionResult> Get2FAStatus()
    {
        try
        {
            var status = await _twoFactorService.GetTwoFactorStatusAsync(User.GetUserId());
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting 2FA status");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> Enable2FA([FromBody] Enable2FARequestDto request)
    {
        try
        {
            var response = await _twoFactorService.InitializeTwoFactorAsync(User.GetUserId(), request.Method);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling 2FA");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("2fa/verify")]
    [Authorize]
    public async Task<IActionResult> Verify2FA([FromBody] Verify2FARequestDto request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(request.Code) || request.Code.Length != 6)
                return BadRequest(new { error = "Invalid verification code format" });

            var status = await _twoFactorService.VerifyTwoFactorAsync(userId, request.Code);
            return Ok(new { success = true, message = "2FA verified and enabled successfully", status });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying 2FA");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> Disable2FA([FromBody] Disable2FARequestDto request)
    {
        try
        {
            var userId = User.GetUserId();
            await _twoFactorService.DisableTwoFactorAsync(userId);
            var status = await _twoFactorService.GetTwoFactorStatusAsync(userId);
            return Ok(new { success = true, message = "2FA disabled successfully", status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling 2FA");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
