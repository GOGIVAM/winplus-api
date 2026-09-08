using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ICourseGamificationService
{
    /// <summary>
    /// À appeler juste après qu'une leçon vient de passer à "complétée"
    /// (CoursePlayerController.SaveProgress). No-op si la gamification n'est
    /// pas activée sur cette formation. Retourne les badges nouvellement
    /// débloqués (pour notification immédiate côté front si besoin).
    /// </summary>
    Task<List<string>> AwardForLessonCompletionAsync(int userId, int courseId, int lessonId, int? quizScorePercent);

    Task<CourseGamificationSettings> GetOrCreateSettingsAsync(int courseId);
    Task<CourseGamificationSettings> UpdateSettingsAsync(int courseId, bool? enabled, int? pointsPerLesson, int? pointsPerQuiz, int? pointsBonusPerfectScore, bool? leaderboardVisible);

    Task<(int Points, List<(string BadgeType, DateTime EarnedAt)> Badges, int? Rank, int TotalStudents)> GetStudentProgressAsync(int courseId, int userId);
    Task<List<(int UserId, string Name, string? AvatarUrl, int Points)>> GetLeaderboardAsync(int courseId, int limit);
}

public class CourseGamificationService : ICourseGamificationService
{
    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;
    private readonly ICourseCertificateService _certificates;
    private readonly ILogger<CourseGamificationService> _logger;

    public CourseGamificationService(ApplicationDbContext db, INtfyService ntfy, ICourseCertificateService certificates, ILogger<CourseGamificationService> logger)
    {
        _db = db;
        _ntfy = ntfy;
        _certificates = certificates;
        _logger = logger;
    }

    public async Task<CourseGamificationSettings> GetOrCreateSettingsAsync(int courseId)
    {
        var settings = await _db.CourseGamificationSettings.FirstOrDefaultAsync(s => s.CourseId == courseId);
        if (settings != null) return settings;

        settings = new CourseGamificationSettings { CourseId = courseId };
        _db.CourseGamificationSettings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task<CourseGamificationSettings> UpdateSettingsAsync(int courseId, bool? enabled, int? pointsPerLesson, int? pointsPerQuiz, int? pointsBonusPerfectScore, bool? leaderboardVisible)
    {
        var settings = await GetOrCreateSettingsAsync(courseId);
        if (enabled.HasValue) settings.GamificationEnabled = enabled.Value;
        if (pointsPerLesson.HasValue) settings.PointsPerLesson = Math.Max(0, pointsPerLesson.Value);
        if (pointsPerQuiz.HasValue) settings.PointsPerQuiz = Math.Max(0, pointsPerQuiz.Value);
        if (pointsBonusPerfectScore.HasValue) settings.PointsBonusPerfectScore = Math.Max(0, pointsBonusPerfectScore.Value);
        if (leaderboardVisible.HasValue) settings.LeaderboardVisible = leaderboardVisible.Value;
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task<List<string>> AwardForLessonCompletionAsync(int userId, int courseId, int lessonId, int? quizScorePercent)
    {
        var settings = await _db.CourseGamificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CourseId == courseId);
        if (settings is not { GamificationEnabled: true }) return new List<string>();

        var lesson = await _db.CourseLessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lessonId);
        var isQuiz = lesson?.LessonType == "quiz";
        var isPerfect = quizScorePercent.HasValue && quizScorePercent.Value >= 100;

        var points = isQuiz ? settings.PointsPerQuiz : settings.PointsPerLesson;
        if (isPerfect) points += settings.PointsBonusPerfectScore;

        var record = await _db.StudentCoursePoints.FirstOrDefaultAsync(p => p.CourseId == courseId && p.UserId == userId);
        if (record == null)
        {
            record = new StudentCoursePoints { CourseId = courseId, UserId = userId };
            _db.StudentCoursePoints.Add(record);
        }
        record.Points += points;
        record.UpdatedAt = DateTime.UtcNow;

        var newBadges = new List<string>();
        async Task TryAward(string badgeType)
        {
            var exists = await _db.StudentCourseBadges.AnyAsync(b => b.CourseId == courseId && b.UserId == userId && b.BadgeType == badgeType);
            if (exists) return;
            _db.StudentCourseBadges.Add(new StudentCourseBadge { CourseId = courseId, UserId = userId, BadgeType = badgeType });
            newBadges.Add(badgeType);
        }

        if (isQuiz) await TryAward("first_quiz");
        if (isPerfect) await TryAward("perfect_score");

        var enrollment = await _db.CourseEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.CourseId == courseId);
        if (enrollment != null)
        {
            if (enrollment.ProgressPercent >= 50) await TryAward("halfway");
            if (enrollment.ProgressPercent >= 100) await TryAward("completed");
        }

        await _db.SaveChangesAsync();

        foreach (var badge in newBadges)
        {
            await _ntfy.PublishAsync($"winplus-user-{userId}", "Badge débloqué !",
                BadgeLabel(badge), userId: userId, type: "CourseGamification");
        }

        // Certificat automatique à 100% (US-3C) — le générer ici (déclenché par
        // la complétion de la dernière leçon) est plus réactif que d'attendre un
        // job périodique ; CourseCertificateService reste idempotent si rappelé.
        if (enrollment?.ProgressPercent >= 100)
        {
            try { await _certificates.GenerateForCompletedCourseAsync(userId, courseId); }
            catch (Exception ex) { _logger.LogError(ex, "Échec génération certificat auto pour user {UserId} course {CourseId}", userId, courseId); }
        }

        return newBadges;
    }

    public async Task<(int Points, List<(string BadgeType, DateTime EarnedAt)> Badges, int? Rank, int TotalStudents)> GetStudentProgressAsync(int courseId, int userId)
    {
        var points = await _db.StudentCoursePoints.AsNoTracking()
            .Where(p => p.CourseId == courseId && p.UserId == userId)
            .Select(p => (int?)p.Points).FirstOrDefaultAsync() ?? 0;

        var badges = await _db.StudentCourseBadges.AsNoTracking()
            .Where(b => b.CourseId == courseId && b.UserId == userId)
            .OrderBy(b => b.EarnedAt)
            .Select(b => new { b.BadgeType, b.EarnedAt })
            .ToListAsync();

        var allPoints = await _db.StudentCoursePoints.AsNoTracking()
            .Where(p => p.CourseId == courseId)
            .OrderByDescending(p => p.Points)
            .Select(p => p.UserId)
            .ToListAsync();

        int? rank = allPoints.Count > 0 && allPoints.Contains(userId) ? allPoints.IndexOf(userId) + 1 : null;

        return (points, badges.Select(b => (b.BadgeType, b.EarnedAt)).ToList(), rank, allPoints.Count);
    }

    public async Task<List<(int UserId, string Name, string? AvatarUrl, int Points)>> GetLeaderboardAsync(int courseId, int limit)
    {
        var rows = await (
            from p in _db.StudentCoursePoints.AsNoTracking()
            join u in _db.Users.AsNoTracking() on p.UserId equals u.Id
            where p.CourseId == courseId
            orderby p.Points descending
            select new { u.Id, Name = u.FirstName + " " + u.LastName, u.AvatarUrl, p.Points }
        ).Take(limit).ToListAsync();

        return rows.Select(r => (r.Id, r.Name.Trim(), r.AvatarUrl, r.Points)).ToList();
    }

    public static string BadgeLabel(string badgeType) => badgeType switch
    {
        "first_quiz" => "Premier quiz validé",
        "halfway" => "Formation 50%",
        "perfect_score" => "Score parfait",
        "completed" => "Formation complétée",
        _ => badgeType,
    };
}
