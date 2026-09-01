using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

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

    public CoursePlayerController(ApplicationDbContext db, ILogger<CoursePlayerController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private async Task<bool> IsEnrolled(int userId, int courseId) =>
        await _db.CourseEnrollments.AnyAsync(e => e.UserId == userId && e.CourseId == courseId && e.IsActive);

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

            return Ok(new
            {
                courseId = course.Id,
                title    = course.Title,
                progressPercent = enrollment?.ProgressPercent ?? 0,
                completedAt     = enrollment?.CompletedAt,
                certificateUrl  = enrollment?.CertificateUrl,
                sections = course.Sections.OrderBy(s => s.Position).Select(s => new
                {
                    s.Id, s.Title, s.Position,
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
            if (!lesson.IsPreview && !await IsEnrolled(userId, courseId))
                return Forbid();

            var prog = await _db.LessonProgress.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId);

            return Ok(new
            {
                lesson.Id, lesson.Title, lesson.LessonType, lesson.Description,
                lesson.VideoUrl, lesson.VideoDurationSec,
                lesson.ArticleContent, lesson.FileUrl, lesson.FileName,
                lesson.IsPreview, lesson.Position,
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

            return Ok(new
            {
                lessonId       = lessonId,
                isCompleted    = prog.IsCompleted,
                progressPercent = enrollment?.ProgressPercent ?? 0,
                courseCompleted = enrollment?.CompletedAt != null,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving progress for lesson {LessonId}", lessonId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

public record SaveProgressRequest(int WatchTimeSec, int LastPositionSec, bool IsCompleted);
