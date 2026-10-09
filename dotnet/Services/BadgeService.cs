using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface IBadgeService
{
    /// <summary>
    /// Évalue tous les badges du catalogue pour un utilisateur et débloque
    /// ceux déjà mérités (idempotent : ne réinsère jamais un badge déjà
    /// débloqué). Appelé au moment de l'action concernée (tentative de quiz
    /// réussie, certificat délivré) et en rattrapage périodique
    /// (BadgeBacklogUnlockService) pour couvrir les cas déjà en base.
    /// </summary>
    Task EvaluateForUserAsync(int userId);
}

/// <summary>
/// Module 24 (lot 8, rattrapage 16.2) : moteur de déblocage des badges
/// plateforme (décision §16.2 du suivi). Chaque critère lit des données déjà
/// réelles en base, aucune nouvelle mesure n'est instrumentée.
///
/// Le calcul de série de jours actifs reprend la même définition qu'un jour
/// « actif » que UsersController.GetProfileStatistics (quiz, session
/// d'étude, téléchargement ou fiche de révision terminée ce jour-là), avec
/// une fenêtre réduite (35 jours au lieu de 180) : suffisante pour les deux
/// seuils du catalogue (7 et 30 jours) et moins coûteuse à recalculer à
/// chaque tentative de quiz.
/// </summary>
public class BadgeService : IBadgeService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<BadgeService> _logger;

    public BadgeService(ApplicationDbContext db, ILogger<BadgeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EvaluateForUserAsync(int userId)
    {
        try
        {
            var defs = await _db.BadgeDefinitions.AsNoTracking()
                .Where(d => d.IsActive)
                .ToDictionaryAsync(d => d.Code, d => d.Id);
            if (defs.Count == 0) return;

            var unlockedCodes = await (
                from ub in _db.UserBadges.AsNoTracking()
                join d in _db.BadgeDefinitions.AsNoTracking() on ub.BadgeDefinitionId equals d.Id
                where ub.UserId == userId
                select d.Code
            ).ToListAsync();
            var toUnlock = new HashSet<string>();

            void Mark(string code)
            {
                if (defs.ContainsKey(code) && !unlockedCodes.Contains(code)) toUnlock.Add(code);
            }

            // ── Quiz ──────────────────────────────────────────────────────
            var passedCount = await _db.QuizAttempts.CountAsync(a => a.UserId == userId && a.Passed);
            if (passedCount >= 1) Mark(BadgeCodes.FirstQuizPassed);
            if (passedCount >= 10) Mark(BadgeCodes.TenQuizzesPassed);

            var hasPerfect = await _db.QuizAttempts.AnyAsync(a => a.UserId == userId && a.Score == 100m);
            if (hasPerfect) Mark(BadgeCodes.PerfectQuizScore);

            // ── Formation ─────────────────────────────────────────────────
            var hasCertificate = await _db.CourseCertificates.AnyAsync(c => c.UserId == userId);
            if (hasCertificate) Mark(BadgeCodes.FirstCourseCompleted);

            // ── Série de jours actifs ────────────────────────────────────
            var streak = await ComputeCurrentStreakAsync(userId);
            if (streak >= 7) Mark(BadgeCodes.Streak7);
            if (streak >= 30) Mark(BadgeCodes.Streak30);

            if (toUnlock.Count == 0) return;

            foreach (var code in toUnlock)
            {
                _db.UserBadges.Add(new UserBadge
                {
                    UserId = userId,
                    BadgeDefinitionId = defs[code],
                    UnlockedAt = DateTime.UtcNow,
                });
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Course entre le hook synchrone et le rattrapage périodique sur le
                // même utilisateur : l'index unique (UserId, BadgeDefinitionId) rejette
                // le doublon, rien d'autre à faire.
            }
            finally
            {
                // Évite l'accumulation d'entités suivies quand ce service est appelé
                // en boucle pour de nombreux utilisateurs (BadgeBacklogUnlockService).
                _db.ChangeTracker.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Badge evaluation failed for user {UserId}", userId);
        }
    }

    private async Task<int> ComputeCurrentStreakAsync(int userId)
    {
        var today = DateTime.UtcNow.Date;
        var since = today.AddDays(-35);
        var activeDates = new HashSet<DateTime>();

        foreach (var d in await _db.QuizAttempts
            .Where(a => a.UserId == userId && a.CompletedAt >= since)
            .Select(a => a.CompletedAt.Date).ToListAsync())
            activeDates.Add(d);

        foreach (var d in await _db.StudySessions
            .Where(s => s.UserId == userId && s.CreatedAt >= since)
            .Select(s => s.CreatedAt.Date).ToListAsync())
            activeDates.Add(d);

        foreach (var d in await _db.DownloadHistories
            .Where(x => x.UserId == userId && x.CreatedAt >= since)
            .Select(x => x.CreatedAt.Date).ToListAsync())
            activeDates.Add(d);

        foreach (var d in await _db.RevisionEnrollments
            .Where(r => r.UserId == userId && r.CompletedAt != null && r.CompletedAt >= since)
            .Select(r => r.CompletedAt!.Value.Date).ToListAsync())
            activeDates.Add(d);

        var streak = 0;
        var cursor = activeDates.Contains(today) ? today : today.AddDays(-1);
        while (activeDates.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }
}
