using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Services;

/// <summary>
/// Service hébergé qui génère le 1er de chaque mois le rapport de coaching
/// WinAI (US-REP-12, Module 6) pour chaque répétiteur actif ayant eu au
/// moins une séance effectuée ou un avis reçu sur le mois écoulé. Même
/// mécanique de planification que <see cref="MonthlyInstitutionReportService"/>.
/// </summary>
public sealed class TutorCoachingReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TutorCoachingReportService> _logger;
    private readonly IHttpClientFactory _httpFactory;

    public TutorCoachingReportService(
        IServiceScopeFactory scopeFactory,
        ILogger<TutorCoachingReportService> logger,
        IHttpClientFactory httpFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TutorCoachingReportService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilFirstOfMonth();
            _logger.LogInformation("Next tutor coaching report run in {Hours}h {Minutes}m.", (int)delay.TotalHours, delay.Minutes);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try { await RunAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "TutorCoachingReportService run failed."); }
        }
    }

    private static TimeSpan ComputeDelayUntilFirstOfMonth()
    {
        var now = DateTime.UtcNow;
        var thisMonthFirst = new DateTime(now.Year, now.Month, 1, 8, 0, 0, DateTimeKind.Utc);
        var next = thisMonthFirst > now ? thisMonthFirst : thisMonthFirst.AddMonths(1);
        return next - now;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var insights = scope.ServiceProvider.GetRequiredService<ITutorInsightsService>();

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var monthEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var tutorUserIds = await db.TutorProfiles
            .Where(p => p.IsActive)
            .Select(p => p.UserId)
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} active tutors for monthly coaching report.", tutorUserIds.Count);

        foreach (var tutorUserId in tutorUserIds)
        {
            if (ct.IsCancellationRequested) break;
            try { await GenerateForTutorAsync(tutorUserId, monthStart, monthEnd, insights, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed coaching report for tutor {TutorUserId}", tutorUserId); }
        }
    }

    private async Task GenerateForTutorAsync(int tutorUserId, DateTime monthStart, DateTime monthEnd, ITutorInsightsService insights, CancellationToken ct)
    {
        var (monthLabel, reviews, subjects, sessionsCount, avgRating) =
            await insights.GetCoachingSourceDataAsync(tutorUserId, monthStart, monthEnd);

        if (reviews.Count == 0 && sessionsCount == 0)
            return; // Rien à rapporter pour ce répétiteur ce mois-ci.

        var client = _httpFactory.CreateClient("FastApiClient");
        var response = await client.PostAsJsonAsync("/api/teacher/coaching-report", new
        {
            month_label = monthLabel,
            reviews,
            subjects_taught = subjects,
            sessions_count = sessionsCount,
            average_rating = avgRating,
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Coaching report generation failed for tutor {TutorUserId}: {Status}", tutorUserId, response.StatusCode);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        await insights.SaveCoachingReportAsync(tutorUserId, monthLabel, body);
        _logger.LogInformation("Coaching report saved for tutor {TutorUserId} — {Month}", tutorUserId, monthLabel);
    }
}
