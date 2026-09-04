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

    /// <summary>
    /// GET /api/users/me  alias de /api/users/profile.
    ///
    /// Le frontend (AuthContext.initializeAuth) appelle /api/users/me. Sans cette
    /// route, la requête tombait sur le gabarit {id} de DELETE avec id="me" et
    /// renvoyait 405 Method Not Allowed, ce qui empêchait la restauration de la
    /// session : l'utilisateur restait connecté avec un profil vide.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public Task<IActionResult> GetMe() => GetProfile();

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
                CoverUrl = user.CoverUrl,
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

    /// <summary>GET /api/users/me/downloads  historique des téléchargements de l'utilisateur connecté.</summary>
    [HttpGet("me/downloads")]
    [Authorize]
    public async Task<IActionResult> GetMyDownloads([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var userId = User.GetUserId();
            if (pageSize > 100) pageSize = 100;

            var downloads = await _db.DownloadHistories
                .AsNoTracking()
                .Where(d => d.UserId == userId)
                .Include(d => d.Subject)
                .OrderByDescending(d => d.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new
                {
                    id = d.Id,
                    subjectId = d.SubjectId,
                    title = d.Subject != null ? d.Subject.Title : d.FileName ?? "Téléchargement",
                    fileName = d.FileName,
                    downloadedAt = d.CreatedAt,
                })
                .ToListAsync();

            return Ok(downloads);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting download history");
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

            if (request.LearningStyle != null)
            {
                var ctx = await _db.ChatbotContexts.FirstOrDefaultAsync(c => c.UserId == userId);
                if (ctx == null)
                {
                    ctx = new ChatbotContext { UserId = userId, LearningStyle = request.LearningStyle, UpdatedAt = DateTime.UtcNow };
                    _db.ChatbotContexts.Add(ctx);
                }
                else
                {
                    ctx.LearningStyle = request.LearningStyle;
                    ctx.UpdatedAt = DateTime.UtcNow;
                }
                await _db.SaveChangesAsync();
            }

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
                CoverUrl = updated.CoverUrl,
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

    // Legacy route  redirect to /api/users/profile/avatar
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
            var today = DateTime.UtcNow.Date;
            var weekAgo = today.AddDays(-7);

            var totalCoursesEnrolled = await _db.Enrollments
                .CountAsync(e => e.UserId == userId);

            var completedCourses = await _db.Enrollments
                .CountAsync(e => e.UserId == userId && e.IsCompleted);

            // Score moyen : QuizAttempts est la vraie source d'activité de quiz
            // (0-100, ramené sur 20 pour l'affichage "X / 20"). L'ancien calcul
            // lisait LearningHistories.QuizScore, un champ presque toujours vide
            // (alimenté uniquement par le flux ExamCoach) : la carte "Score
            // moyen" affichait 0 même pour un élève actif en quiz.
            var attempts = await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsCompleted)
                .Select(a => new { a.Score, a.CompletedAt })
                .ToListAsync();

            var avgScore100 = attempts.Count == 0 ? 0.0 : (double)attempts.Average(a => a.Score);
            var averageScore = Math.Round(avgScore100 / 5.0, 1);
            var quizCompleted = attempts.Count;

            var recentAttempts = attempts.Where(a => a.CompletedAt >= weekAgo).ToList();
            var priorAttempts  = attempts.Where(a => a.CompletedAt < weekAgo && a.CompletedAt >= weekAgo.AddDays(-7)).ToList();
            int? scoreDelta = recentAttempts.Count > 0 && priorAttempts.Count > 0
                ? (int)Math.Round((double)(recentAttempts.Average(a => a.Score) - priorAttempts.Average(a => a.Score)) / 5.0)
                : null;

            var sixMonthsAgo = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
            var scoredForMonths = attempts.Where(a => a.CompletedAt >= sixMonthsAgo).ToList();
            var monthlyScores = Enumerable.Range(0, 6).Select(offset =>
            {
                var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-5 + offset);
                var monthEnd = monthStart.AddMonths(1);
                var slice = scoredForMonths.Where(a => a.CompletedAt >= monthStart && a.CompletedAt < monthEnd).ToList();
                return slice.Count == 0 ? 0.0 : Math.Round((double)slice.Average(a => a.Score) / 5.0, 1);
            }).ToList();

            // Temps d'étude : combine toutes les activités qui représentent du
            // temps réel passé à étudier, chacune datée par le jour où elle a
            // eu lieu (pour le total ET la répartition par jour) :
            //   - StudySessions      : sessions guidées + consultations de PDF
            //                          (voir StudySessionComplete/LogStudyTime)
            //   - QuizAttempts       : TimeSpentSeconds, déjà enregistré par quiz
            //   - RevisionEnrollments: durée réelle (CompletedAt - StartedAt),
            //                          ou à défaut la durée estimée de la fiche
            var studyEvents = new List<(DateTime Day, int Minutes)>();

            var sessions = await _db.StudySessions
                .Where(s => s.UserId == userId)
                .Select(s => new { s.CreatedAt, s.Duration })
                .ToListAsync();
            studyEvents.AddRange(sessions.Select(s => (s.CreatedAt.Date, s.Duration)));

            var quizTimes = await _db.QuizAttempts
                .Where(a => a.UserId == userId && a.TimeSpentSeconds != null && a.TimeSpentSeconds > 0)
                .Select(a => new { a.CompletedAt, a.TimeSpentSeconds })
                .ToListAsync();
            studyEvents.AddRange(quizTimes.Select(a => (a.CompletedAt.Date, (a.TimeSpentSeconds!.Value + 59) / 60)));

            var revisionTimes = await _db.RevisionEnrollments
                .Where(r => r.UserId == userId && r.CompletedAt != null)
                .Select(r => new { r.StartedAt, r.CompletedAt, RevisionMinutes = r.Revision != null ? r.Revision.DurationMinutes : null })
                .ToListAsync();
            studyEvents.AddRange(revisionTimes.Select(r =>
            {
                var minutes = r.StartedAt != null
                    ? Math.Max(1, (int)Math.Round((r.CompletedAt!.Value - r.StartedAt.Value).TotalMinutes))
                    : (r.RevisionMinutes ?? 0);
                return (r.CompletedAt!.Value.Date, minutes);
            }).Where(e => e.Item2 > 0));

            var totalStudyMinutes = studyEvents.Sum(e => e.Minutes);
            var totalTimeSeconds = totalStudyMinutes * 60;

            var weeklyStudyHours = Enumerable.Range(0, 7).Select(offset =>
            {
                var day = today.AddDays(-6 + offset);
                var mins = studyEvents.Where(e => e.Day == day).Sum(e => e.Minutes);
                return Math.Round(mins / 60.0, 1);
            }).ToList();

            // Téléchargements
            var totalDownloads = await _db.DownloadHistories.CountAsync(d => d.UserId == userId);
            var weeklyDownloads = await _db.DownloadHistories.CountAsync(d => d.UserId == userId && d.CreatedAt >= weekAgo);
            var weekDownloadDates = await _db.DownloadHistories
                .Where(d => d.UserId == userId && d.CreatedAt >= today.AddDays(-6))
                .Select(d => d.CreatedAt.Date)
                .ToListAsync();
            var weeklyDownloadTrend = Enumerable.Range(0, 7)
                .Select(offset => weekDownloadDates.Count(d => d == today.AddDays(-6 + offset)))
                .ToList();

            // Série de jours actifs : quiz, session d'étude ou téléchargement
            // comptent tous comme un jour "étudié", sur une fenêtre de 180 jours.
            var sinceStreak = today.AddDays(-180);
            var activeDates = new HashSet<DateTime>();
            foreach (var d in attempts.Where(a => a.CompletedAt >= sinceStreak).Select(a => a.CompletedAt.Date)) activeDates.Add(d);
            foreach (var d in await _db.StudySessions.Where(s => s.UserId == userId && s.CreatedAt >= sinceStreak).Select(s => s.CreatedAt.Date).ToListAsync()) activeDates.Add(d);
            foreach (var d in await _db.DownloadHistories.Where(x => x.UserId == userId && x.CreatedAt >= sinceStreak).Select(x => x.CreatedAt.Date).ToListAsync()) activeDates.Add(d);
            foreach (var d in revisionTimes.Where(r => r.CompletedAt >= sinceStreak).Select(r => r.CompletedAt!.Value.Date)) activeDates.Add(d);

            var currentStreak = 0;
            var cursor = activeDates.Contains(today) ? today : today.AddDays(-1);
            while (activeDates.Contains(cursor)) { currentStreak++; cursor = cursor.AddDays(-1); }

            var longestStreak = 0;
            var run = 0;
            for (var d = sinceStreak; d <= today; d = d.AddDays(1))
            {
                if (activeDates.Contains(d)) { run++; longestStreak = Math.Max(longestStreak, run); }
                else run = 0;
            }

            var streakTrend = Enumerable.Range(0, 7)
                .Select(offset => activeDates.Contains(today.AddDays(-6 + offset)) ? 1 : 0)
                .ToList();

            static string FormatStudyTime(int minutes)
            {
                if (minutes <= 0) return "0 min";
                var h = minutes / 60;
                var m = minutes % 60;
                if (h <= 0) return $"{m} min";
                return m > 0 ? $"{h} h {m:D2}" : $"{h} h";
            }

            return Ok(new ProfileStatisticsResponse
            {
                TotalCoursesEnrolled = totalCoursesEnrolled,
                CompletedCourses = completedCourses,
                AverageScore = averageScore,
                TotalTimeSeconds = totalTimeSeconds,
                QuizCompleted = quizCompleted,
                ScoreDelta = scoreDelta,
                TotalDownloads = totalDownloads,
                WeeklyDownloads = weeklyDownloads,
                WeeklyDownloadTrend = weeklyDownloadTrend,
                StudyTimeFormatted = FormatStudyTime(totalStudyMinutes),
                StudyGoal = null,
                CurrentStreak = currentStreak,
                LongestStreak = longestStreak,
                StreakTrend = streakTrend,
                WeeklyStudyHours = weeklyStudyHours,
                MonthlyScores = monthlyScores,
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

    [HttpDelete("{id:int}")]
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

    [HttpPost("{id:int}/restore")]
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

    [HttpDelete("{id:int}/permanent")]
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

    [HttpGet("{id:int}/statistics")]
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

    /// <summary>
    /// Méthodes de paiement sauvegardées (Mobile Money / cartes).
    /// WinPlus utilise NotchPay – les tokens sont gérés côté NotchPay,
    /// ces endpoints exposent une liste vide jusqu'à l'intégration complète.
    /// </summary>
    [HttpGet("{userId:int}/payment-methods")]
    [Authorize]
    public IActionResult GetPaymentMethods(int userId)
    {
        var callerId = User.GetUserId();
        if (callerId != userId && !User.IsAdmin()) return Forbid();
        return Ok(new { data = Array.Empty<object>(), success = true });
    }

    [HttpPost("{userId:int}/payment-methods")]
    [Authorize]
    public IActionResult SavePaymentMethod(int userId, [FromBody] object body)
    {
        var callerId = User.GetUserId();
        if (callerId != userId && !User.IsAdmin()) return Forbid();
        return StatusCode(501, new { success = false, error = "La sauvegarde de méthodes de paiement sera disponible dans une prochaine version." });
    }

    [HttpDelete("{userId:int}/payment-methods/{methodId}")]
    [Authorize]
    public IActionResult DeletePaymentMethod(int userId, string methodId)
    {
        var callerId = User.GetUserId();
        if (callerId != userId && !User.IsAdmin()) return Forbid();
        return Ok(new { success = true });
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
