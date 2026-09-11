using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// CRUD complet des formations pour les enseignants.
/// GET    /api/teacher/courses
/// POST   /api/teacher/courses
/// PUT    /api/teacher/courses/{id}
/// DELETE /api/teacher/courses/{id}
/// POST   /api/teacher/courses/{id}/submit
/// POST   /api/teacher/courses/{id}/sections
/// PUT    /api/teacher/courses/{id}/sections/{sId}
/// DELETE /api/teacher/courses/{id}/sections/{sId}
/// PUT    /api/teacher/courses/{id}/sections/reorder
/// POST   /api/teacher/courses/{id}/sections/{sId}/lessons
/// PUT    /api/teacher/courses/{id}/sections/{sId}/lessons/{lId}
/// DELETE /api/teacher/courses/{id}/sections/{sId}/lessons/{lId}
/// PUT    /api/teacher/courses/{id}/sections/{sId}/lessons/reorder
/// </summary>
[ApiController]
[Authorize]
[Route("api/teacher/courses")]
public class TeacherCourseController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TeacherCourseController> _logger;
    private readonly ICourseGamificationService _gamification;
    private readonly INtfyService _ntfy;
    private readonly IHttpClientFactory _httpClientFactory;

    public TeacherCourseController(ApplicationDbContext db, ILogger<TeacherCourseController> logger, ICourseGamificationService gamification, INtfyService ntfy, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _logger = logger;
        _gamification = gamification;
        _ntfy = ntfy;
        _httpClientFactory = httpClientFactory;
    }

    private async Task<Course?> OwnCourse(int courseId, int teacherId) =>
        await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId && c.InstructorId == teacherId);

    // ── Liste ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List()
    {
        try
        {
            var teacherId = User.GetUserId();
            var courses = await _db.Courses.AsNoTracking()
                .Where(c => c.InstructorId == teacherId)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new
                {
                    c.Id, c.Title, c.Slug, c.Status, c.Price, c.IsFree,
                    c.LessonsCount, c.EnrolledCount, c.AvgRating, c.ReviewsCount,
                    c.ThumbnailUrl, c.Category, c.Level, c.CreatedAt, c.UpdatedAt,
                    c.RejectionReason,
                })
                .ToListAsync();
            return Ok(courses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing teacher courses");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Détail complet d'une formation pour l'éditeur (sections + toutes les
    /// leçons, publiées ou non). Le frontend (TeacherFormations.tsx,
    /// SubjectActionModal.tsx) appelait déjà cet endpoint, mais il n'existait
    /// pas encore — l'éditeur de formation ne pouvait jamais charger le
    /// détail d'une formation existante (404 silencieux). Corrigé ici.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            var course = await _db.Courses.AsNoTracking()
                .Where(c => c.Id == id && c.InstructorId == teacherId)
                .Include(c => c.Instructor)
                .Include(c => c.Sections.OrderBy(s => s.Position))
                    .ThenInclude(s => s.Lessons.OrderBy(l => l.Position))
                .FirstOrDefaultAsync();

            if (course == null) return NotFound(new { error = "Formation introuvable" });

            return Ok(new
            {
                course.Id, course.Title, course.Slug, course.Description, course.ShortDescription,
                course.ThumbnailUrl, course.PreviewVideoUrl, course.Language, course.Level,
                course.Category, course.Tags, course.Price, course.IsFree, course.IsIncludedInSub,
                course.Status, course.RejectionReason,
                course.TotalDurationMin, course.LessonsCount, course.EnrolledCount,
                course.AvgRating, course.ReviewsCount, course.Requirements, course.Objectives,
                course.CertificateEnabled, course.CanalMessagerie, course.CreatedAt,
                instructor = new
                {
                    id = course.Instructor.Id,
                    name = course.Instructor.FirstName + " " + course.Instructor.LastName,
                    avatarUrl = course.Instructor.AvatarUrl,
                },
                sections = course.Sections.OrderBy(s => s.Position).Select(s => new
                {
                    s.Id, s.Title, s.Description, s.Position,
                    s.UnlockRule, s.DelayDays, s.MinScore,
                    lessons = s.Lessons.OrderBy(l => l.Position).Select(l => new
                    {
                        l.Id, l.Title, l.Description, l.LessonType, l.VideoUrl, l.VideoDurationSec,
                        l.ArticleContent, l.FileUrl, l.FileName, l.IsPreview, l.IsPublished, l.Position,
                        l.SourceSubjectId,
                        checkpoints = string.IsNullOrEmpty(l.CheckpointsJson)
                            ? new List<CheckpointDto>()
                            : System.Text.Json.JsonSerializer.Deserialize<List<CheckpointDto>>(l.CheckpointsJson) ?? new(),
                    }),
                }),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting course detail {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Création ─────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CourseUpsertRequest req)
    {
        try
        {
            var teacherId = User.GetUserId();
            var slug = Slugify(req.Title ?? "formation");

            // Unicité du slug
            var existing = await _db.Courses.AnyAsync(c => c.Slug == slug);
            if (existing) slug = slug + "-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var course = new Course
            {
                Title            = req.Title ?? "Nouvelle formation",
                Slug             = slug,
                Description      = req.Description,
                ShortDescription = req.ShortDescription,
                ThumbnailUrl     = req.ThumbnailUrl,
                PreviewVideoUrl  = req.PreviewVideoUrl,
                Language         = req.Language ?? "fr",
                Level            = req.Level ?? "debutant",
                Category         = req.Category,
                Tags             = req.Tags ?? new(),
                Price            = req.Price ?? 0,
                IsFree           = req.IsFree ?? false,
                IsIncludedInSub  = req.IsIncludedInSub ?? false,
                Requirements     = req.Requirements ?? new(),
                Objectives       = req.Objectives ?? new(),
                CertificateEnabled = req.CertificateEnabled ?? true,
                InstructorId     = teacherId,
                Status           = "draft",
            };

            _db.Courses.Add(course);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(List), new { }, new { course.Id, course.Slug });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating course");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Mise à jour ──────────────────────────────────────────────────────────

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CourseUpsertRequest req)
    {
        try
        {
            var teacherId = User.GetUserId();
            var course    = await OwnCourse(id, teacherId);
            if (course == null) return NotFound(new { error = "Formation introuvable" });
            if (course.Status == "pending_review")
                return BadRequest(new { error = "Impossible de modifier une formation en cours de validation" });

            if (req.Title            != null) course.Title            = req.Title;
            if (req.Description      != null) course.Description      = req.Description;
            if (req.ShortDescription != null) course.ShortDescription = req.ShortDescription;
            if (req.ThumbnailUrl     != null) course.ThumbnailUrl     = req.ThumbnailUrl;
            if (req.PreviewVideoUrl  != null) course.PreviewVideoUrl  = req.PreviewVideoUrl;
            if (req.Language         != null) course.Language         = req.Language;
            if (req.Level            != null) course.Level            = req.Level;
            if (req.Category         != null) course.Category         = req.Category;
            if (req.Tags             != null) course.Tags             = req.Tags;
            if (req.Price            != null) course.Price            = req.Price.Value;
            if (req.IsFree           != null) course.IsFree           = req.IsFree.Value;
            if (req.IsIncludedInSub  != null) course.IsIncludedInSub  = req.IsIncludedInSub.Value;
            if (req.Requirements     != null) course.Requirements     = req.Requirements;
            if (req.Objectives       != null) course.Objectives       = req.Objectives;
            if (req.CertificateEnabled != null) course.CertificateEnabled = req.CertificateEnabled.Value;
            course.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Formation mise à jour" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Suppression ──────────────────────────────────────────────────────────

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            var course    = await OwnCourse(id, teacherId);
            if (course == null) return NotFound(new { error = "Formation introuvable" });
            if (course.Status != "draft")
                return BadRequest(new { error = "Seules les formations en brouillon peuvent être supprimées. Archivez les autres." });

            _db.Courses.Remove(course);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Formation supprimée" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Soumettre à validation ───────────────────────────────────────────────

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            var course    = await OwnCourse(id, teacherId);
            if (course == null) return NotFound(new { error = "Formation introuvable" });
            if (course.Status != "draft" && course.Status != "archived")
                return BadRequest(new { error = "La formation doit être en brouillon pour être soumise" });

            var hasLessons = await _db.CourseLessons.AnyAsync(l => l.CourseId == id && l.IsPublished);
            if (!hasLessons)
                return BadRequest(new { error = "Ajoutez au moins une leçon publiée avant de soumettre" });

            course.Status          = "pending_review";
            course.RejectionReason = null;
            course.UpdatedAt       = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Formation soumise à validation" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Sections ─────────────────────────────────────────────────────────────

    [HttpPost("{id}/sections")]
    public async Task<IActionResult> AddSection(int id, [FromBody] SectionRequest req)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var maxPos = await _db.CourseSections.Where(s => s.CourseId == id)
                .Select(s => (int?)s.Position).MaxAsync() ?? -1;

            var section = new CourseSection
            {
                CourseId    = id,
                Title       = req.Title,
                Description = req.Description,
                Position    = maxPos + 1,
                UnlockRule  = NormalizeUnlockRule(req.UnlockRule),
                DelayDays   = req.DelayDays,
                MinScore    = req.MinScore,
            };
            _db.CourseSections.Add(section);
            await _db.SaveChangesAsync();
            return Ok(new { section.Id, section.Title, section.Position });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding section to course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("{id}/sections/{sId}")]
    public async Task<IActionResult> UpdateSection(int id, int sId, [FromBody] SectionRequest req)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var section = await _db.CourseSections.FirstOrDefaultAsync(s => s.Id == sId && s.CourseId == id);
            if (section == null) return NotFound(new { error = "Section introuvable" });

            section.Title       = req.Title;
            section.Description = req.Description;
            section.UnlockRule  = NormalizeUnlockRule(req.UnlockRule);
            section.DelayDays   = req.DelayDays;
            section.MinScore    = req.MinScore;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Section mise à jour" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating section {SId}", sId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("{id}/sections/{sId}")]
    public async Task<IActionResult> DeleteSection(int id, int sId)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var section = await _db.CourseSections.FirstOrDefaultAsync(s => s.Id == sId && s.CourseId == id);
            if (section == null) return NotFound(new { error = "Section introuvable" });

            _db.CourseSections.Remove(section);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Section supprimée" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting section {SId}", sId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("{id}/sections/reorder")]
    public async Task<IActionResult> ReorderSections(int id, [FromBody] List<ReorderItem> items)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var sections = await _db.CourseSections.Where(s => s.CourseId == id).ToListAsync();
            foreach (var item in items)
            {
                var s = sections.FirstOrDefault(s => s.Id == item.Id);
                if (s != null) s.Position = item.Position;
            }
            await _db.SaveChangesAsync();
            return Ok(new { message = "Sections réordonnées" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering sections for course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Leçons ───────────────────────────────────────────────────────────────

    [HttpPost("{id}/sections/{sId}/lessons")]
    public async Task<IActionResult> AddLesson(int id, int sId, [FromBody] LessonRequest req)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var section = await _db.CourseSections.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sId && s.CourseId == id);
            if (section == null) return NotFound(new { error = "Section introuvable" });

            var maxPos = await _db.CourseLessons.Where(l => l.SectionId == sId)
                .Select(l => (int?)l.Position).MaxAsync() ?? -1;

            var lesson = new CourseLesson
            {
                SectionId       = sId,
                CourseId        = id,
                Title           = req.Title ?? "Nouvelle leçon",
                Description     = req.Description,
                LessonType      = req.LessonType ?? "video",
                VideoUrl        = req.VideoUrl,
                VideoDurationSec = req.VideoDurationSec,
                ArticleContent  = req.ArticleContent,
                FileUrl         = req.FileUrl,
                FileName        = req.FileName,
                IsPreview       = req.IsPreview ?? false,
                IsPublished     = req.IsPublished ?? true,
                Position        = maxPos + 1,
                CheckpointsJson = req.Checkpoints is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(req.Checkpoints) : null,
            };
            _db.CourseLessons.Add(lesson);
            await _db.SaveChangesAsync();
            return Ok(new { lesson.Id, lesson.Title, lesson.Position });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding lesson to section {SId}", sId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// "Ajouter à une formation" (Module 2, US-CAT-03) : insère une épreuve/
    /// correction déjà achetée (ou gratuite, ou publiée par ce professeur)
    /// comme leçon de type "Fichier". SourceSubjectId sert aussi à calculer
    /// "X enseignants ont utilisé ce contenu" (SubjectsController.GetById).
    /// </summary>
    [HttpPost("{id}/sections/{sId}/lessons/from-subject")]
    public async Task<IActionResult> AddLessonFromSubject(int id, int sId, [FromBody] AddLessonFromSubjectRequest req)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var section = await _db.CourseSections.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sId && s.CourseId == id);
            if (section == null) return NotFound(new { error = "Section introuvable" });

            var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == req.SubjectId && !s.IsDeleted);
            if (subject == null) return NotFound(new { error = "Contenu introuvable" });

            var owns = subject.Price <= 0
                || subject.AuthorUserId == teacherId
                || await _db.OrderItems.AnyAsync(oi => oi.SubjectId == subject.Id
                    && oi.Order.UserId == teacherId && oi.Order.Status == "completed");
            if (!owns)
                return StatusCode(403, new { error = "Achète ce contenu avant de l'ajouter à une formation." });

            var maxPos = await _db.CourseLessons.Where(l => l.SectionId == sId)
                .Select(l => (int?)l.Position).MaxAsync() ?? -1;

            var lesson = new CourseLesson
            {
                SectionId = sId,
                CourseId = id,
                Title = subject.Title,
                Description = subject.Description,
                LessonType = "Fichier",
                SourceSubjectId = subject.Id,
                IsPublished = true,
                Position = maxPos + 1,
            };
            _db.CourseLessons.Add(lesson);
            await _db.SaveChangesAsync();
            return Ok(new { lesson.Id, lesson.Title, lesson.Position, lesson.SourceSubjectId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding lesson from subject to section {SId}", sId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("{id}/sections/{sId}/lessons/{lId}")]
    public async Task<IActionResult> UpdateLesson(int id, int sId, int lId, [FromBody] LessonRequest req)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var lesson = await _db.CourseLessons
                .FirstOrDefaultAsync(l => l.Id == lId && l.SectionId == sId && l.CourseId == id);
            if (lesson == null) return NotFound(new { error = "Leçon introuvable" });

            if (req.Title           != null) lesson.Title           = req.Title;
            if (req.Description     != null) lesson.Description     = req.Description;
            if (req.LessonType      != null) lesson.LessonType      = req.LessonType;
            if (req.VideoUrl        != null) lesson.VideoUrl        = req.VideoUrl;
            if (req.VideoDurationSec != null) lesson.VideoDurationSec = req.VideoDurationSec;
            if (req.ArticleContent  != null) lesson.ArticleContent  = req.ArticleContent;
            if (req.FileUrl         != null) lesson.FileUrl         = req.FileUrl;
            if (req.FileName        != null) lesson.FileName        = req.FileName;
            if (req.IsPreview       != null) lesson.IsPreview       = req.IsPreview.Value;
            if (req.IsPublished     != null) lesson.IsPublished     = req.IsPublished.Value;
            if (req.Checkpoints     != null) lesson.CheckpointsJson = req.Checkpoints.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(req.Checkpoints) : null;
            lesson.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Leçon mise à jour" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating lesson {LId}", lId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("{id}/sections/{sId}/lessons/{lId}")]
    public async Task<IActionResult> DeleteLesson(int id, int sId, int lId)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var lesson = await _db.CourseLessons
                .FirstOrDefaultAsync(l => l.Id == lId && l.SectionId == sId && l.CourseId == id);
            if (lesson == null) return NotFound(new { error = "Leçon introuvable" });

            _db.CourseLessons.Remove(lesson);
            await _db.SaveChangesAsync();
            return Ok(new { message = "Leçon supprimée" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting lesson {LId}", lId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPut("{id}/sections/{sId}/lessons/reorder")]
    public async Task<IActionResult> ReorderLessons(int id, int sId, [FromBody] List<ReorderItem> items)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var lessons = await _db.CourseLessons.Where(l => l.SectionId == sId && l.CourseId == id).ToListAsync();
            foreach (var item in items)
            {
                var l = lessons.FirstOrDefault(l => l.Id == item.Id);
                if (l != null) l.Position = item.Position;
            }
            await _db.SaveChangesAsync();
            return Ok(new { message = "Leçons réordonnées" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering lessons for section {SId}", sId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Gamification (Module 5, 5C) ─────────────────────────────────────────

    [HttpGet("{id}/gamification")]
    public async Task<IActionResult> GetGamificationSettings(int id)
    {
        var teacherId = User.GetUserId();
        if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

        var s = await _gamification.GetOrCreateSettingsAsync(id);
        return Ok(new
        {
            s.GamificationEnabled, s.PointsPerLesson, s.PointsPerQuiz,
            s.PointsBonusPerfectScore, s.LeaderboardVisible,
        });
    }

    [HttpPut("{id}/gamification")]
    public async Task<IActionResult> UpdateGamificationSettings(int id, [FromBody] GamificationSettingsRequest req)
    {
        var teacherId = User.GetUserId();
        if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

        var s = await _gamification.UpdateSettingsAsync(id, req.GamificationEnabled, req.PointsPerLesson, req.PointsPerQuiz, req.PointsBonusPerfectScore, req.LeaderboardVisible);
        return Ok(new
        {
            s.GamificationEnabled, s.PointsPerLesson, s.PointsPerQuiz,
            s.PointsBonusPerfectScore, s.LeaderboardVisible,
        });
    }

    // ── Analytics par leçon (audit 5A, point 4) ─────────────────────────────

    [HttpGet("{id}/analytics/lessons")]
    public async Task<IActionResult> GetLessonAnalytics(int id)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });

            var enrolledCount = await _db.CourseEnrollments.CountAsync(e => e.CourseId == id && e.IsActive);
            var lessons = await _db.CourseLessons.AsNoTracking()
                .Where(l => l.CourseId == id)
                .OrderBy(l => l.Position)
                .ToListAsync();

            var lessonIds = lessons.Select(l => l.Id).ToList();
            var progressByLesson = await _db.LessonProgress.AsNoTracking()
                .Where(p => lessonIds.Contains(p.LessonId))
                .GroupBy(p => p.LessonId)
                .Select(g => new { LessonId = g.Key, Completed = g.Count(p => p.IsCompleted), AvgWatchSec = g.Average(p => (double)p.WatchTimeSec) })
                .ToDictionaryAsync(x => x.LessonId);

            var quizIds = lessons.Where(l => l.QuizId.HasValue).Select(l => l.QuizId!.Value).Distinct().ToList();
            var avgScoreByQuiz = quizIds.Count == 0 ? new Dictionary<int, double>() : await _db.QuizAttempts.AsNoTracking()
                .Where(a => quizIds.Contains(a.QuizId))
                .GroupBy(a => a.QuizId)
                .Select(g => new { QuizId = g.Key, Avg = g.Average(a => (double)a.Score) })
                .ToDictionaryAsync(x => x.QuizId, x => x.Avg);

            var result = lessons.Select(l =>
            {
                progressByLesson.TryGetValue(l.Id, out var prog);
                double? avgQuizScore = l.QuizId.HasValue && avgScoreByQuiz.TryGetValue(l.QuizId.Value, out var s) ? s : null;
                return new
                {
                    lessonId = l.Id,
                    title = l.Title,
                    lessonType = l.LessonType,
                    completionRate = enrolledCount == 0 ? 0 : Math.Round(100.0 * (prog?.Completed ?? 0) / enrolledCount, 1),
                    completedCount = prog?.Completed ?? 0,
                    enrolledCount,
                    averageWatchTimeSec = prog == null ? 0 : Math.Round(prog.AvgWatchSec, 0),
                    averageQuizScore = avgQuizScore.HasValue ? Math.Round(avgQuizScore.Value, 1) : (double?)null,
                    // Alerte WinAI (US-FOR-06) : taux de complétion sous 40%.
                    lowCompletionAlert = enrolledCount > 0 && (100.0 * (prog?.Completed ?? 0) / enrolledCount) < 40,
                };
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lesson analytics for course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Décrochage (US-FOR-07, Module 9) ────────────────────────────────────

    private async Task<List<(int UserId, string Name, string? AvatarUrl, DateTime LastActivity, int DaysInactive)>> ComputeInactiveStudentsAsync(int courseId, int thresholdDays)
    {
        var enrollments = await _db.CourseEnrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId && e.IsActive && e.CompletedAt == null)
            .Include(e => e.User)
            .ToListAsync();

        var userIds = enrollments.Select(e => e.UserId).ToList();
        var lastProgressByUser = await _db.LessonProgress.AsNoTracking()
            .Where(p => p.CourseId == courseId && userIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Last = g.Max(p => p.UpdatedAt) })
            .ToDictionaryAsync(x => x.UserId, x => x.Last);

        var now = DateTime.UtcNow;
        var result = new List<(int, string, string?, DateTime, int)>();
        foreach (var e in enrollments)
        {
            var lastActivity = lastProgressByUser.TryGetValue(e.UserId, out var last) ? last : e.EnrolledAt;
            var daysInactive = (int)(now - lastActivity).TotalDays;
            if (daysInactive >= thresholdDays)
                result.Add((e.UserId, $"{e.User.FirstName} {e.User.LastName}".Trim(), e.User.AvatarUrl, lastActivity, daysInactive));
        }
        return result.OrderByDescending(r => r.Item5).ToList();
    }

    /// <summary>Configure le seuil d'inactivité (jours) déclenchant l'alerte "élèves à risque".</summary>
    [HttpPut("{id}/inactivity-threshold")]
    public async Task<IActionResult> SetInactivityThreshold(int id, [FromBody] SetInactivityThresholdRequest req)
    {
        var teacherId = User.GetUserId();
        var course = await OwnCourse(id, teacherId);
        if (course == null) return NotFound(new { error = "Formation introuvable" });
        if (req.Days is < 1 or > 90) return BadRequest(new { error = "Le seuil doit être entre 1 et 90 jours." });

        course.InactivityThresholdDays = req.Days;
        await _db.SaveChangesAsync();
        return Ok(new { inactivityThresholdDays = course.InactivityThresholdDays });
    }

    /// <summary>Élèves inactifs depuis au moins le seuil configuré (US-FOR-07).</summary>
    [HttpGet("{id}/inactive-students")]
    public async Task<IActionResult> GetInactiveStudents(int id)
    {
        var teacherId = User.GetUserId();
        var course = await OwnCourse(id, teacherId);
        if (course == null) return NotFound(new { error = "Formation introuvable" });

        var inactive = await ComputeInactiveStudentsAsync(id, course.InactivityThresholdDays);
        return Ok(inactive.Select(s => new { userId = s.UserId, name = s.Name, avatarUrl = s.AvatarUrl, lastActivity = s.LastActivity, daysInactive = s.DaysInactive }));
    }

    /// <summary>
    /// Envoie un message de relance WinAI aux élèves inactifs sélectionnés
    /// (ou tous les élèves inactifs si aucune sélection). Enregistre l'envoi
    /// pour mesurer le taux de réactivation ultérieurement.
    /// </summary>
    [HttpPost("{id}/relaunch-students")]
    public async Task<IActionResult> RelaunchStudents(int id, [FromBody] RelaunchStudentsRequest req)
    {
        var teacherId = User.GetUserId();
        var course = await OwnCourse(id, teacherId);
        if (course == null) return NotFound(new { error = "Formation introuvable" });

        var inactive = await ComputeInactiveStudentsAsync(id, course.InactivityThresholdDays);
        var targets = req.UserIds is { Count: > 0 }
            ? inactive.Where(s => req.UserIds.Contains(s.UserId)).ToList()
            : inactive;
        if (targets.Count == 0) return Ok(new { sent = 0 });

        string messageText;
        try
        {
            var client = _httpClientFactory.CreateClient("FastApiClient");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/inactivity-relaunch-message")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { course_title = course.Title })
            };
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth)) httpReq.Headers.TryAddWithoutValidation("Authorization", auth);
            var res = await client.SendAsync(httpReq);
            var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            messageText = json.TryGetProperty("message_text", out var m) ? m.GetString() ?? "" : "";
        }
        catch
        {
            messageText = "";
        }
        if (string.IsNullOrWhiteSpace(messageText))
            messageText = $"On continue « {course.Title} » ? Reprends où tu t'es arrêté(e), je suis là si tu as des questions !";

        var sent = 0;
        foreach (var s in targets)
        {
            _db.DirectMessages.Add(new DirectMessage { FromUserId = teacherId, ToUserId = s.UserId, Content = messageText });
            _db.CourseInactivityRelaunches.Add(new CourseInactivityRelaunch { CourseId = id, UserId = s.UserId });
            sent++;
        }
        await _db.SaveChangesAsync();

        return Ok(new { sent, message = messageText });
    }

    /// <summary>Taux de réactivation des relances envoyées (US-FOR-07).</summary>
    [HttpGet("{id}/relaunch-stats")]
    public async Task<IActionResult> GetRelaunchStats(int id)
    {
        var teacherId = User.GetUserId();
        var course = await OwnCourse(id, teacherId);
        if (course == null) return NotFound(new { error = "Formation introuvable" });

        var relaunches = await _db.CourseInactivityRelaunches.AsNoTracking()
            .Where(r => r.CourseId == id).ToListAsync();
        if (relaunches.Count == 0) return Ok(new { totalSent = 0, reactivatedCount = 0, reactivationRate = (double?)null });

        var userIds = relaunches.Select(r => r.UserId).Distinct().ToList();
        var lastProgressByUser = await _db.LessonProgress.AsNoTracking()
            .Where(p => p.CourseId == id && userIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Last = g.Max(p => p.UpdatedAt) })
            .ToDictionaryAsync(x => x.UserId, x => x.Last);

        var reactivated = relaunches.Count(r =>
            lastProgressByUser.TryGetValue(r.UserId, out var last) && last > r.SentAt);

        return Ok(new
        {
            totalSent = relaunches.Count,
            reactivatedCount = reactivated,
            reactivationRate = Math.Round(100.0 * reactivated / relaunches.Count, 1),
        });
    }

    // ── Syllabus WinAI (US-FOR-09, Module 9) ────────────────────────────────

    /// <summary>Génère un plan de cours WinAI semaine par semaine (avant création de la formation).</summary>
    [HttpPost("generate-syllabus")]
    public async Task<IActionResult> GenerateSyllabus([FromBody] object body, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/generate-syllabus")
            {
                Content = System.Net.Http.Json.JsonContent.Create(body)
            };
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
            var res = await client.SendAsync(req, ct);
            Response.StatusCode = (int)res.StatusCode;
            return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Sans ce filtre, une panne/timeout du service IA remontait comme
            // une 500 générique ("Une erreur serveur interne s'est produite")
            // indiscernable d'un vrai bug applicatif côté client.
            _logger.LogError(ex, "Service IA injoignable pour generate-syllabus");
            return StatusCode(503, new { success = false, error = "Le service de génération WinAI est temporairement indisponible. Réessaie dans quelques instants." });
        }
    }

    /// <summary>
    /// Importe un syllabus généré (ou édité) comme structure de sections/leçons
    /// de la formation : une section par semaine, une leçon "article" reprenant
    /// notions/activités/ressources — le professeur complète ensuite (vidéo,
    /// fichiers…) depuis l'éditeur habituel.
    /// </summary>
    [HttpPost("{id}/import-syllabus")]
    public async Task<IActionResult> ImportSyllabus(int id, [FromBody] ImportSyllabusRequest request)
    {
        try
        {
            var teacherId = User.GetUserId();
            if (await OwnCourse(id, teacherId) == null) return NotFound(new { error = "Formation introuvable" });
            if (request.Weeks == null || request.Weeks.Count == 0)
                return BadRequest(new { error = "Aucune semaine à importer." });

            var maxPos = await _db.CourseSections.Where(s => s.CourseId == id)
                .Select(s => (int?)s.Position).MaxAsync() ?? -1;

            var createdSectionIds = new List<int>();
            foreach (var week in request.Weeks.OrderBy(w => w.Week))
            {
                maxPos++;
                var section = new CourseSection
                {
                    CourseId = id,
                    Title = $"Semaine {week.Week} — {week.Title}",
                    Position = maxPos,
                };
                _db.CourseSections.Add(section);
                await _db.SaveChangesAsync();

                var articleBody = string.Join("\n\n", new[]
                {
                    week.Concepts is { Count: > 0 } ? "**Notions :** " + string.Join(", ", week.Concepts) : null,
                    week.Activities is { Count: > 0 } ? "**Activités :** " + string.Join(", ", week.Activities) : null,
                    week.SuggestedResources is { Count: > 0 } ? "**Ressources suggérées :** " + string.Join(", ", week.SuggestedResources) : null,
                }.Where(s => s != null));

                _db.CourseLessons.Add(new CourseLesson
                {
                    SectionId = section.Id,
                    CourseId = id,
                    Title = week.Title,
                    LessonType = "article",
                    ArticleContent = articleBody,
                    IsPublished = false,
                    Position = 0,
                });
                createdSectionIds.Add(section.Id);
            }
            await _db.SaveChangesAsync();

            return Ok(new { sectionsCreated = createdSectionIds.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing syllabus for course {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly string[] ValidUnlockRules = { "immediate", "delay_days", "min_score" };

    private static string NormalizeUnlockRule(string? rule) =>
        !string.IsNullOrWhiteSpace(rule) && ValidUnlockRules.Contains(rule) ? rule : "immediate";

    private static string Slugify(string title)
    {
        var slug = title.ToLower()
            .Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
            .Replace("à", "a").Replace("â", "a").Replace("ä", "a")
            .Replace("ù", "u").Replace("û", "u").Replace("ü", "u")
            .Replace("ô", "o").Replace("ö", "o").Replace("î", "i").Replace("ï", "i")
            .Replace("ç", "c").Replace("ñ", "n");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9]+", "-");
        return slug.Trim('-');
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class CourseUpsertRequest
{
    public string?       Title            { get; set; }
    public string?       Description      { get; set; }
    public string?       ShortDescription { get; set; }
    public string?       ThumbnailUrl     { get; set; }
    public string?       PreviewVideoUrl  { get; set; }
    public string?       Language         { get; set; }
    public string?       Level            { get; set; }
    public string?       Category         { get; set; }
    public List<string>? Tags             { get; set; }
    public decimal?      Price            { get; set; }
    public bool?         IsFree           { get; set; }
    public bool?         IsIncludedInSub  { get; set; }
    public List<string>? Requirements     { get; set; }
    public List<string>? Objectives       { get; set; }
    public bool?         CertificateEnabled { get; set; }
}

public record SectionRequest(string Title, string? Description, string? UnlockRule = null, int? DelayDays = null, int? MinScore = null);

public class LessonRequest
{
    public string?  Title            { get; set; }
    public string?  Description      { get; set; }
    public string?  LessonType       { get; set; }
    public string?  VideoUrl         { get; set; }
    public int?     VideoDurationSec  { get; set; }
    public string?  ArticleContent   { get; set; }
    public string?  FileUrl          { get; set; }
    public string?  FileName         { get; set; }
    public bool?    IsPreview        { get; set; }
    public bool?    IsPublished      { get; set; }
    public List<CheckpointDto>? Checkpoints { get; set; }
}

/// <summary>Checkpoint vidéo (Module 5B) : quiz déclenché à un instant précis du player.</summary>
public class CheckpointDto
{
    public int TimestampMs { get; set; }
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public int BonneReponseIndex { get; set; }
}

public record GamificationSettingsRequest(bool? GamificationEnabled, int? PointsPerLesson, int? PointsPerQuiz, int? PointsBonusPerfectScore, bool? LeaderboardVisible);
public record ReorderItem(int Id, int Position);
public class AddLessonFromSubjectRequest
{
    public int SubjectId { get; set; }
}

public record SetInactivityThresholdRequest(int Days);
public record RelaunchStudentsRequest(List<int>? UserIds);

public class SyllabusWeekDto
{
    public int Week { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<string>? Concepts { get; set; }
    public List<string>? Activities { get; set; }
    public int DurationMinutes { get; set; }
    public List<string>? SuggestedResources { get; set; }
}

public class ImportSyllabusRequest
{
    public List<SyllabusWeekDto>? Weeks { get; set; }
}
