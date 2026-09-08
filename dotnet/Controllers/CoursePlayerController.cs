using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Lecture des formations (curriculum + progression par leçon).
/// GET  /api/courses/{id}/play                      → curriculum + progression
/// GET  /api/courses/{id}/play/{lessonId}            → contenu d'une leçon
/// POST /api/courses/{id}/play/{lessonId}/progress   → sauvegarder progression
/// </summary>
[ApiController]
[Authorize]
[Route("api/courses/{courseId}/play")]
public class CoursePlayerController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CoursePlayerController> _logger;
    private readonly ICourseAccessService _access;
    private readonly ICourseGamificationService _gamification;

    public CoursePlayerController(ApplicationDbContext db, ILogger<CoursePlayerController> logger, ICourseAccessService access, ICourseGamificationService gamification)
    {
        _db = db;
        _logger = logger;
        _access = access;
        _gamification = gamification;
    }

    private async Task<bool> IsEnrolled(int userId, int courseId) =>
        await _db.CourseEnrollments.AnyAsync(e => e.UserId == userId && e.CourseId == courseId && e.IsActive);

    /// <summary>Accès élève par section (endpoint dédié, prompt_prof.md 5B : GET /api/formations/{id}/acces-eleve).</summary>
    [HttpGet("~/api/courses/{courseId}/acces-eleve")]
    public async Task<IActionResult> GetAccesEleve(int courseId)
    {
        try
        {
            var userId = User.GetUserId();
            var enrollment = await _db.CourseEnrollments.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId && e.IsActive);
            if (enrollment == null) return Forbid();

            var sections = await _db.CourseSections.AsNoTracking()
                .Where(s => s.CourseId == courseId)
                .Include(s => s.Lessons)
                .OrderBy(s => s.Position)
                .ToListAsync();

            var access = await _access.ComputeSectionAccessAsync(userId, courseId, enrollment.EnrolledAt, sections);

            return Ok(sections.Select(s => new
            {
                sectionId = s.Id,
                title = s.Title,
                unlockRule = s.UnlockRule,
                isUnlocked = access[s.Id].Unlocked,
                lockReason = access[s.Id].Reason,
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting student access for course {CourseId}", courseId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCurriculum(int courseId)
    {
        try
        {
            var userId = User.GetUserId();
            if (!await IsEnrolled(userId, courseId))
                return Forbid();

            var course = await _db.Courses.AsNoTracking()
                .Where(c => c.Id == courseId)
                .Include(c => c.Sections.OrderBy(s => s.Position))
                    .ThenInclude(s => s.Lessons.Where(l => l.IsPublished).OrderBy(l => l.Position))
                .FirstOrDefaultAsync();

            if (course == null) return NotFound(new { error = "Formation introuvable" });

            // Progressions de l'utilisateur
            var progresses = await _db.LessonProgress.AsNoTracking()
                .Where(p => p.UserId == userId && p.CourseId == courseId)
                .Select(p => new { p.LessonId, p.IsCompleted, p.LastPositionSec, p.WatchTimeSec })
                .ToListAsync();
            var progressMap = progresses.ToDictionary(p => p.LessonId);

            var enrollment = await _db.CourseEnrollments.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);

            var orderedSections = course.Sections.OrderBy(s => s.Position).ToList();
            var access = enrollment != null
                ? await _access.ComputeSectionAccessAsync(userId, courseId, enrollment.EnrolledAt, orderedSections)
                : orderedSections.ToDictionary(s => s.Id, _ => (true, (string?)null));

            return Ok(new
            {
                courseId = course.Id,
                title    = course.Title,
                progressPercent = enrollment?.ProgressPercent ?? 0,
                completedAt     = enrollment?.CompletedAt,
                certificateUrl  = enrollment?.CertificateUrl,
                sections = orderedSections.Select(s => new
                {
                    s.Id, s.Title, s.Position,
                    unlockRule = s.UnlockRule,
                    isUnlocked = access[s.Id].Item1,
                    lockReason = access[s.Id].Item2,
                    lessons = s.Lessons.OrderBy(l => l.Position).Select(l =>
                    {
                        progressMap.TryGetValue(l.Id, out var prog);
                        return new
                        {
                            l.Id, l.Title, l.LessonType, l.VideoDurationSec, l.Position, l.IsPreview,
                            isCompleted     = prog?.IsCompleted ?? false,
                            lastPositionSec = prog?.LastPositionSec ?? 0,
                            watchTimeSec    = prog?.WatchTimeSec ?? 0,
                        };
                    }),
                }),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting curriculum for course {CourseId}", courseId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("{lessonId}")]
    public async Task<IActionResult> GetLesson(int courseId, int lessonId)
    {
        try
        {
            var userId = User.GetUserId();

            var lesson = await _db.CourseLessons.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == lessonId && l.CourseId == courseId && l.IsPublished);
            if (lesson == null) return NotFound(new { error = "Leçon introuvable" });

            // Preview accessible sans inscription
            var enrollment = await _db.CourseEnrollments.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId && e.IsActive);
            if (!lesson.IsPreview && enrollment == null)
                return Forbid();

            if (enrollment != null)
            {
                var sections = await _db.CourseSections.AsNoTracking()
                    .Where(s => s.CourseId == courseId).Include(s => s.Lessons)
                    .OrderBy(s => s.Position).ToListAsync();
                var access = await _access.ComputeSectionAccessAsync(userId, courseId, enrollment.EnrolledAt, sections);
                if (access.TryGetValue(lesson.SectionId, out var sectionAccess) && !sectionAccess.Unlocked)
                    return StatusCode(403, new { error = sectionAccess.Reason ?? "Cette section n'est pas encore débloquée." });
            }

            var prog = await _db.LessonProgress.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId);

            return Ok(new
            {
                lesson.Id, lesson.Title, lesson.LessonType, lesson.Description,
                lesson.VideoUrl, lesson.VideoDurationSec,
                lesson.ArticleContent, lesson.FileUrl, lesson.FileName,
                lesson.IsPreview, lesson.Position, lesson.QuizId,
                checkpoints = string.IsNullOrEmpty(lesson.CheckpointsJson)
                    ? new List<CheckpointDto>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<CheckpointDto>>(lesson.CheckpointsJson) ?? new(),
                progress = prog == null ? null : new
                {
                    prog.IsCompleted, prog.LastPositionSec, prog.WatchTimeSec, prog.CompletedAt,
                },
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lesson {LessonId}", lessonId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("{lessonId}/progress")]
    public async Task<IActionResult> SaveProgress(int courseId, int lessonId, [FromBody] SaveProgressRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            if (!await IsEnrolled(userId, courseId))
                return Forbid();

            var lesson = await _db.CourseLessons.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == lessonId && l.CourseId == courseId);
            if (lesson == null) return NotFound(new { error = "Leçon introuvable" });

            var prog = await _db.LessonProgress
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId);

            var wasAlreadyCompleted = prog?.IsCompleted ?? false;

            if (prog == null)
            {
                prog = new LessonProgress
                {
                    UserId    = userId,
                    LessonId  = lessonId,
                    CourseId  = courseId,
                };
                _db.LessonProgress.Add(prog);
            }

            prog.WatchTimeSec    = Math.Max(prog.WatchTimeSec, req.WatchTimeSec);
            prog.LastPositionSec = req.LastPositionSec;
            prog.UpdatedAt       = DateTime.UtcNow;

            if (req.IsCompleted && !prog.IsCompleted)
            {
                prog.IsCompleted = true;
                prog.CompletedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            // Recalculer la progression globale via la fonction SQL
            await _db.Database.ExecuteSqlRawAsync(
                "SELECT recalculate_enrollment_progress({0}, {1})", userId, courseId);

            // Reload pour renvoyer la progression à jour
            var enrollment = await _db.CourseEnrollments.AsNoTracking()
                .FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);

            // Gamification (Module 5, 5C) : points + badges + certificat auto,
            // uniquement au moment où la leçon passe réellement à "complétée"
            // (jamais sur un ré-appel idempotent).
            List<string> newBadges = new();
            if (req.IsCompleted && !wasAlreadyCompleted)
                newBadges = await _gamification.AwardForLessonCompletionAsync(userId, courseId, lessonId, req.QuizScorePercent);

            return Ok(new
            {
                lessonId       = lessonId,
                isCompleted    = prog.IsCompleted,
                progressPercent = enrollment?.ProgressPercent ?? 0,
                courseCompleted = enrollment?.CompletedAt != null,
                newBadges,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving progress for lesson {LessonId}", lessonId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Widget de progression gamifiée de l'élève (US-3C) : points, badges, rang.</summary>
    [HttpGet("~/api/courses/{courseId}/gamification/me")]
    public async Task<IActionResult> GetMyGamification(int courseId)
    {
        var userId = User.GetUserId();
        if (!await IsEnrolled(userId, courseId)) return Forbid();

        var settings = await _gamification.GetOrCreateSettingsAsync(courseId);
        if (!settings.GamificationEnabled)
            return Ok(new { enabled = false });

        var (points, badges, rank, total) = await _gamification.GetStudentProgressAsync(courseId, userId);
        return Ok(new
        {
            enabled = true,
            points,
            badges = badges.Select(b => new { type = b.BadgeType, label = CourseGamificationService.BadgeLabel(b.BadgeType), earnedAt = b.EarnedAt }),
            rank,
            totalStudents = total,
            leaderboardVisible = settings.LeaderboardVisible,
        });
    }

    /// <summary>Classement de la formation, si le professeur l'a rendu visible.</summary>
    [HttpGet("~/api/courses/{courseId}/leaderboard")]
    public async Task<IActionResult> GetLeaderboard(int courseId)
    {
        var userId = User.GetUserId();
        if (!await IsEnrolled(userId, courseId)) return Forbid();

        var settings = await _gamification.GetOrCreateSettingsAsync(courseId);
        if (!settings.GamificationEnabled || !settings.LeaderboardVisible)
            return Ok(new List<object>());

        var rows = await _gamification.GetLeaderboardAsync(courseId, 50);
        return Ok(rows.Select((r, i) => new { rank = i + 1, userId = r.UserId, name = r.Name.Trim(), avatarUrl = r.AvatarUrl, points = r.Points, isMe = r.UserId == userId }));
    }
}

public record SaveProgressRequest(int WatchTimeSec, int LastPositionSec, bool IsCompleted, int? QuizScorePercent = null);
