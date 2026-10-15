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
    private readonly IStorageService _storage;

    // Module 27 (lot 8) : URL présignée à durée limitée pour la vidéo/le fichier
    // d'une leçon de formation, jamais de lien S3 permanent. 60 minutes couvre
    // la durée d'une leçon sans forcer un renouvellement en cours de lecture.
    private static readonly TimeSpan LessonMediaTtl = TimeSpan.FromMinutes(60);

    public CoursePlayerController(ApplicationDbContext db, ILogger<CoursePlayerController> logger, ICourseAccessService access, ICourseGamificationService gamification, IStorageService storage)
    {
        _db = db;
        _logger = logger;
        _storage = storage;
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

            // Lot 9.7b (F12) : première ouverture de la leçon par un inscrit
            // mémorisée côté serveur (LessonProgress.CreatedAt). C'est le point
            // de départ des durées minimales vérifiées à la complétion : un
            // appel direct à l'API ne peut plus déclarer une leçon terminée
            // sans l'avoir ouverte.
            if (enrollment != null && prog == null)
            {
                try
                {
                    _db.LessonProgress.Add(new LessonProgress
                    {
                        UserId = userId, LessonId = lessonId, CourseId = courseId,
                        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    });
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    // Ouverture concurrente (deux onglets) : la ligne existe déjà.
                    _logger.LogDebug(ex, "Progression déjà créée pour la leçon {LessonId}", lessonId);
                    foreach (var entry in _db.ChangeTracker.Entries<LessonProgress>().Where(e => e.State == EntityState.Added).ToList())
                        entry.State = EntityState.Detached;
                }
            }

            // Module 27 (lot 8) : jamais de lien S3 permanent vers la vidéo ou le
            // fichier d'une leçon  toujours une URL de lecture présignée à
            // durée limitée, sur le même principe que SubjectsController pour
            // les épreuves.
            var videoUrl = string.IsNullOrWhiteSpace(lesson.VideoUrl)
                ? null
                : _storage.PresignGetUrl(lesson.VideoUrl, LessonMediaTtl);
            // Lot 9.7b (F13, 18.21) : une épreuve ou un livre du catalogue
            // ajouté à la formation (SourceSubjectId) n'est jamais
            // téléchargeable : aucune adresse de fichier, il s'ouvre dans la
            // visionneuse sécurisée (GET /api/subjects/{id}/view?courseId=…,
            // filigrane nominatif). Les autres fichiers de leçon restent
            // téléchargeables (URL présignée).
            var viewerOnly = lesson.SourceSubjectId != null;
            var fileUrl = viewerOnly || string.IsNullOrWhiteSpace(lesson.FileUrl)
                ? null
                : _storage.PresignGetUrl(lesson.FileUrl, LessonMediaTtl);

            return Ok(new
            {
                lesson.Id, lesson.Title, lesson.LessonType, lesson.Description,
                videoUrl, lesson.VideoDurationSec,
                lesson.ArticleContent, fileUrl, lesson.FileName,
                sourceSubjectId = lesson.SourceSubjectId,
                viewerOnly,
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
            var now = DateTime.UtcNow;

            if (prog == null)
            {
                prog = new LessonProgress
                {
                    UserId    = userId,
                    LessonId  = lessonId,
                    CourseId  = courseId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.LessonProgress.Add(prog);
            }

            // Lot 9.7b (F12) : le temps de visionnage n'est plus pris tel que
            // déclaré. Il ne peut progresser que du temps réellement écoulé
            // depuis la sauvegarde précédente (lecture accélérée jusqu'à 2×,
            // plus une marge) : sauter à la fin de la vidéo ou appeler l'API
            // directement ne crédite pas le visionnage.
            prog.WatchTimeSec    = LessonCompletionRules.CreditedWatchTime(prog.WatchTimeSec, req.WatchTimeSec, prog.UpdatedAt, now);
            prog.LastPositionSec = Math.Max(0, req.LastPositionSec);
            prog.UpdatedAt       = now;

            // Lot 9.7b (F12) : la complétion est validée par le serveur
            // (visionnage minimal d'une vidéo, quiz réussi, temps minimal de
            // lecture), et le score de quiz transmis à la gamification est
            // relu dans les tentatives, jamais pris du client.
            int? serverQuizScore = null;
            string? completionRefusal = null;
            if (req.IsCompleted && !prog.IsCompleted)
            {
                var check = await LessonCompletionRules.CheckAsync(_db, userId, lesson, prog, now);
                completionRefusal = check.Refusal;
                serverQuizScore = check.QuizScorePercent;
                if (completionRefusal == null)
                {
                    prog.IsCompleted = true;
                    prog.CompletedAt = now;
                }
            }

            await _db.SaveChangesAsync();

            if (completionRefusal != null)
            {
                var current = await _db.CourseEnrollments.AsNoTracking()
                    .Where(e => e.UserId == userId && e.CourseId == courseId)
                    .Select(e => new { e.ProgressPercent, e.CompletedAt })
                    .FirstOrDefaultAsync();
                return Conflict(new
                {
                    code = "completion_not_validated",
                    error = completionRefusal,
                    lessonId,
                    isCompleted = false,
                    progressPercent = current?.ProgressPercent ?? 0,
                    courseCompleted = current?.CompletedAt != null,
                });
            }

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
            if (prog.IsCompleted && !wasAlreadyCompleted)
                newBadges = await _gamification.AwardForLessonCompletionAsync(userId, courseId, lessonId, serverQuizScore);

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

        // Module 35 : classement anonymisé par défaut pour un élève, même sur
        // sa propre ligne ("Toi" plutôt que son vrai nom)  seul le professeur
        // (TeacherCourseController.GetLeaderboard) voit les vraies identités.
        var rows = await _gamification.GetLeaderboardAsync(courseId, 50, revealRealNames: false);
        return Ok(rows.Select((r, i) => new
        {
            rank = i + 1,
            userId = r.UserId,
            name = r.UserId == userId ? "Toi" : r.Name.Trim(),
            avatarUrl = r.UserId == userId ? r.AvatarUrl : null,
            points = r.Points,
            isMe = r.UserId == userId,
        }));
    }
}

/// <remarks>Lot 9.7b (F12) : <c>QuizScorePercent</c> est accepté pour compatibilité mais ignoré (score relu côté serveur).</remarks>
public record SaveProgressRequest(int WatchTimeSec, int LastPositionSec, bool IsCompleted, int? QuizScorePercent = null);

/// <summary>
/// Lot 9.7b (F12) : règles serveur de complétion d'une leçon de formation.
///  * vidéo : visionnage crédité d'au moins 80 % de la durée connue (ou, sans
///    durée connue, 30 secondes depuis l'ouverture) ;
///  * quiz : une tentative réussie du quiz de la leçon (QuizAttempts) ;
///  * article, fichier, ressource du catalogue : 20 secondes au moins depuis
///    la première ouverture de la leçon (GET /play/{lessonId}).
/// </summary>
public static class LessonCompletionRules
{
    public const double MinVideoWatchRatio = 0.8;
    public const int MinSecondsWithoutDuration = 30;
    public const int MinReadingSeconds = 20;
    /// <summary>Lecture accélérée tolérée (2×) et marge réseau entre deux sauvegardes.</summary>
    public const int MaxPlaybackRate = 2;
    public const int WatchSlackSec = 20;

    public static int CreditedWatchTime(int previous, int declared, DateTime lastUpdate, DateTime now)
    {
        var elapsed = Math.Max(0, (now - lastUpdate).TotalSeconds);
        var ceiling = previous + (int)Math.Ceiling(elapsed * MaxPlaybackRate) + WatchSlackSec;
        return Math.Max(previous, Math.Min(Math.Max(0, declared), ceiling));
    }

    public static async Task<(string? Refusal, int? QuizScorePercent)> CheckAsync(
        ApplicationDbContext db, int userId, CourseLesson lesson, LessonProgress prog, DateTime now)
    {
        var type = (lesson.LessonType ?? string.Empty).Trim().ToLowerInvariant();
        var openedFor = (now - prog.CreatedAt).TotalSeconds;

        if (type == "quiz" && lesson.QuizId != null)
        {
            var quiz = await db.Quizzes.AsNoTracking()
                .Where(q => q.Id == lesson.QuizId.Value)
                .Select(q => new { q.PassingScore })
                .FirstOrDefaultAsync();
            var passing = quiz?.PassingScore ?? 60;
            var attempts = await db.QuizAttempts.AsNoTracking()
                .Where(a => a.UserId == userId && a.QuizId == lesson.QuizId.Value)
                .Select(a => new { a.Score, a.Passed })
                .ToListAsync();
            if (attempts.Count == 0)
                return ("Réponds au quiz de cette leçon pour la terminer.", null);
            var best = attempts.Max(a => a.Score);
            if (!attempts.Any(a => a.Passed || a.Score >= passing))
                return ($"Quiz non réussi (meilleur score {best:0} %, minimum {passing} %) : réessaie pour terminer la leçon.", (int)Math.Round(best));
            return (null, (int)Math.Round(best));
        }

        if (type == "video")
        {
            if (lesson.VideoDurationSec is > 0)
            {
                var required = (int)Math.Ceiling(lesson.VideoDurationSec.Value * MinVideoWatchRatio);
                if (prog.WatchTimeSec < required)
                    return ($"Regarde au moins {MinVideoWatchRatio:P0} de la vidéo pour la terminer ({prog.WatchTimeSec / 60} min {prog.WatchTimeSec % 60:00} s sur {required / 60} min {required % 60:00} s).", null);
                return (null, null);
            }
            if (openedFor < MinSecondsWithoutDuration)
                return ("Regarde la vidéo avant de la marquer comme terminée.", null);
            return (null, null);
        }

        if (openedFor < MinReadingSeconds)
            return ("Prends le temps de consulter cette leçon avant de la marquer comme terminée.", null);
        return (null, null);
    }
}
