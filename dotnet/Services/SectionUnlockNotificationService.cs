using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Service hébergé (prompt_prof.md Module 5B) : débloque automatiquement les
/// sections dont la condition de drip content est remplie et notifie
/// l'élève. L'accès réel reste calculé en temps réel par
/// <see cref="ICourseAccessService"/> à chaque requête — ce job ne fait que
/// détecter la transition verrouillé→déverrouillé pour déclencher une
/// notification une seule fois par élève et par section.
/// </summary>
public sealed class SectionUnlockNotificationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SectionUnlockNotificationService> _logger;

    public SectionUnlockNotificationService(IServiceScopeFactory scopeFactory, ILogger<SectionUnlockNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SectionUnlockNotificationService started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "SectionUnlockNotificationService run failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<ICourseAccessService>();
        var ntfy = scope.ServiceProvider.GetRequiredService<INtfyService>();

        // Uniquement les sections avec une règle de déblocage réelle — pas la peine
        // d'itérer les formations sans drip content.
        var gatedCourseIds = await db.CourseSections
            .Where(s => s.UnlockRule != "immediate")
            .Select(s => s.CourseId).Distinct().ToListAsync(ct);

        foreach (var courseId in gatedCourseIds)
        {
            if (ct.IsCancellationRequested) break;

            var sections = await db.CourseSections.AsNoTracking()
                .Where(s => s.CourseId == courseId).Include(s => s.Lessons)
                .OrderBy(s => s.Position).ToListAsync(ct);

            var enrollments = await db.CourseEnrollments.AsNoTracking()
                .Where(e => e.CourseId == courseId && e.IsActive)
                .ToListAsync(ct);

            foreach (var enrollment in enrollments)
            {
                var result = await access.ComputeSectionAccessAsync(enrollment.UserId, courseId, enrollment.EnrolledAt, sections);
                foreach (var section in sections.Where(s => s.UnlockRule != "immediate"))
                {
                    if (!result.TryGetValue(section.Id, out var state) || !state.Unlocked) continue;

                    var alreadyNotified = await db.SectionUnlockNotifications
                        .AnyAsync(n => n.UserId == enrollment.UserId && n.SectionId == section.Id, ct);
                    if (alreadyNotified) continue;

                    db.SectionUnlockNotifications.Add(new SectionUnlockNotification
                    {
                        UserId = enrollment.UserId,
                        SectionId = section.Id,
                    });
                    await ntfy.PublishAsync($"winplus-user-{enrollment.UserId}", "Nouvelle section débloquée !",
                        $"« {section.Title} » est maintenant disponible.", userId: enrollment.UserId, type: "CourseSection");
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
