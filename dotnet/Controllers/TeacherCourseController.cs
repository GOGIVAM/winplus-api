using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

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

    public TeacherCourseController(ApplicationDbContext db, ILogger<TeacherCourseController> logger)
    {
        _db = db;
        _logger = logger;
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

    // ── Helpers ──────────────────────────────────────────────────────────────

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

public record SectionRequest(string Title, string? Description);

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
}

public record ReorderItem(int Id, int Position);
