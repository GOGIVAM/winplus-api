using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Backend.Data;

namespace Backend.Services;

/// <summary>
/// Service hébergé qui génère et envoie le 1er de chaque mois un rapport mensuel
/// de pilotage aux institutions (Role = "organization") actives.
/// </summary>
public sealed class MonthlyInstitutionReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyInstitutionReportService> _logger;
    private readonly IHttpClientFactory _httpFactory;

    public MonthlyInstitutionReportService(
        IServiceScopeFactory scopeFactory,
        ILogger<MonthlyInstitutionReportService> logger,
        IHttpClientFactory httpFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MonthlyInstitutionReportService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilFirstOfMonth();
            _logger.LogInformation(
                "Next monthly institution report in {Hours}h {Minutes}m.",
                (int)delay.TotalHours, delay.Minutes);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyInstitutionReportService run failed.");
            }
        }
    }

    private static TimeSpan ComputeDelayUntilFirstOfMonth()
    {
        var now = DateTime.UtcNow;
        // 1st of next month at 07:00 UTC
        var next = new DateTime(now.Year, now.Month, 1, 7, 0, 0, DateTimeKind.Utc)
            .AddMonths(1);
        // If we're past the 1st at 07:00, next month; if not yet this 1st, use this month's 1st
        var thisMonthFirst = new DateTime(now.Year, now.Month, 1, 7, 0, 0, DateTimeKind.Utc);
        if (thisMonthFirst > now) next = thisMonthFirst;
        return next - now;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running MonthlyInstitutionReport at {Time}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        // Find all active institution users (Role = "organization")
        var institutions = await db.Users
            .Where(u => u.Role == "organization" && u.IsActive && !u.IsDeleted && u.IsEmailVerified)
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} institution users for monthly report.", institutions.Count);

        foreach (var inst in institutions)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await SendMonthlyReportAsync(inst.Id, inst.Email, inst.FirstName ?? "Directeur", db, email, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send monthly report to institution {Email}", inst.Email);
            }
        }
    }

    private async Task SendMonthlyReportAsync(
        int institutionUserId,
        string institutionEmail,
        string directorName,
        ApplicationDbContext db,
        IEmailService emailService,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var monthEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthLabel = monthStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

        // Find students linked to this institution via InstitutionId FK on User (if exists)
        // Fallback: query all students supervised by this org user
        List<int> studentIds;
        try
        {
            studentIds = await db.Users
                .Where(u => u.Role == "student" && !u.IsDeleted && EF.Property<int?>(u, "InstitutionId") == institutionUserId)
                .Select(u => u.Id)
                .ToListAsync(ct);
        }
        catch
        {
            // InstitutionId column may not exist — skip detailed stats
            studentIds = new List<int>();
        }

        // Monthly scores
        var monthScores = await db.DailyScores
            .Where(s => studentIds.Contains(s.UserId) && s.CreatedAt >= monthStart && s.CreatedAt < monthEnd)
            .ToListAsync(ct);

        var avgScore = monthScores.Any() ? Math.Round(monthScores.Average(s => (double)s.AverageScore), 1) : 0.0;
        var activeStudents = monthScores.Select(s => s.UserId).Distinct().Count();
        var passRate = monthScores.Any()
            ? Math.Round(monthScores.GroupBy(s => s.UserId)
                .Count(g => g.Average(s => (double)s.AverageScore) >= 50) / (double)Math.Max(studentIds.Count, 1) * 100, 1)
            : 0.0;
        var totalQuizzes = monthScores.Sum(s => s.QuizCount);

        // Build AI narrative via Python (optional — fire-and-forget)
        string aiNarrative;
        try
        {
            var pyClient = _httpFactory.CreateClient("FastApiClient");
            var payload = new
            {
                institution_id = institutionUserId,
                student_ids = studentIds,
                institution_name = directorName,
            };
            var response = await pyClient.PostAsJsonAsync("/api/institution/action-plan", payload, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                var actions = json.TryGetProperty("actions", out var a) ? a.ToString() : "";
                aiNarrative = $"WinAI recommande pour {monthLabel} :\n{actions}";
            }
            else
            {
                aiNarrative = "Analyse WinAI indisponible pour ce rapport.";
            }
        }
        catch
        {
            aiNarrative = "Analyse WinAI indisponible pour ce rapport.";
        }

        // Build HTML email body
        var body = BuildReportHtml(
            directorName, monthLabel, studentIds.Count,
            activeStudents, avgScore, passRate, totalQuizzes, aiNarrative);

        await emailService.SendGenericEmailAsync(
            to: institutionEmail,
            subject: $"[WinPlus] Rapport mensuel de pilotage — {monthLabel}",
            htmlBody: body,
            cancellationToken: ct);

        _logger.LogInformation(
            "Monthly report sent to {Email} for {Month} ({Students} students).",
            institutionEmail, monthLabel, studentIds.Count);
    }

    private static string BuildReportHtml(
        string directorName,
        string monthLabel,
        int totalStudents,
        int activeStudents,
        double avgScore,
        double passRate,
        int totalQuizzes,
        string aiNarrative)
    {
        var engagementRate = totalStudents > 0
            ? Math.Round((double)activeStudents / totalStudents * 100, 1) : 0;
        var aiNarrativeHtml = aiNarrative.Replace("\n", "<br/>");

        return $@"<!DOCTYPE html>
<html lang=""fr"">
<head>
  <meta charset=""utf-8""/>
  <style>
    body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #f8fafc; margin: 0; padding: 0; }}
    .wrap {{ max-width: 600px; margin: 0 auto; background: #fff; border-radius: 12px; overflow: hidden; }}
    .header {{ background: #0f766e; padding: 28px 32px; }}
    .header h1 {{ color: #fff; font-size: 22px; margin: 0 0 4px; }}
    .header p {{ color: #99f6e4; font-size: 14px; margin: 0; }}
    .body {{ padding: 28px 32px; }}
    .kpi-row {{ display: flex; gap: 12px; margin-bottom: 20px; flex-wrap: wrap; }}
    .kpi {{ flex: 1; min-width: 120px; background: #f0fdfa; border: 1px solid #99f6e4;
             border-radius: 8px; padding: 14px 16px; text-align: center; }}
    .kpi .value {{ font-size: 26px; font-weight: 800; color: #0f766e; line-height: 1; }}
    .kpi .label {{ font-size: 11px; color: #6b7280; margin-top: 4px; }}
    .section {{ margin-bottom: 20px; }}
    .section h3 {{ font-size: 15px; font-weight: 700; color: #1f2937; margin: 0 0 10px; }}
    .ai-box {{ background: #1e293b; border-radius: 8px; padding: 16px 20px; color: #e2e8f0; font-size: 13px; line-height: 1.6; }}
    .footer {{ background: #f8fafc; padding: 16px 32px; text-align: center; font-size: 12px; color: #9ca3af; }}
    .badge {{ display: inline-block; background: #dcfce7; color: #166534; border-radius: 999px;
               padding: 2px 10px; font-size: 12px; font-weight: 600; margin-left: 6px; }}
  </style>
</head>
<body>
<div class=""wrap"">
  <div class=""header"">
    <h1>Rapport mensuel WinPlus</h1>
    <p>Espace Institution · {monthLabel} · Généré automatiquement par WinAI</p>
  </div>
  <div class=""body"">
    <p style=""font-size:15px;color:#374151"">Bonjour <strong>{directorName}</strong>,</p>
    <p style=""font-size:13px;color:#6b7280;margin-top:4px"">Voici le bilan de pilotage pédagogique de votre établissement pour {monthLabel}.</p>

    <div class=""kpi-row"">
      <div class=""kpi""><div class=""value"">{totalStudents}</div><div class=""label"">Étudiants inscrits</div></div>
      <div class=""kpi""><div class=""value"">{activeStudents}</div><div class=""label"">Actifs ce mois</div></div>
      <div class=""kpi""><div class=""value"">{avgScore}%</div><div class=""label"">Score moyen</div></div>
      <div class=""kpi""><div class=""value"">{passRate}%</div><div class=""label"">Taux de réussite</div></div>
      <div class=""kpi""><div class=""value"">{engagementRate}%</div><div class=""label"">Engagement</div></div>
      <div class=""kpi""><div class=""value"">{totalQuizzes}</div><div class=""label"">Quiz complétés</div></div>
    </div>

    <div class=""section"">
      <h3>Plan d'action WinAI</h3>
      <div class=""ai-box"">{aiNarrativeHtml}</div>
    </div>

    <p style=""font-size:12px;color:#9ca3af"">
      Pour consulter l'analyse complète, connectez-vous à votre espace institution sur
      <a href=""https://winplus.cm/organisation"" style=""color:#0f766e"">WinPlus</a>.
    </p>
  </div>
  <div class=""footer"">
    WinPlus · Plateforme éducative · <a href=""https://winplus.cm"" style=""color:#0f766e"">winplus.cm</a>
    <br/>Ce rapport est généré automatiquement le 1er de chaque mois.
  </div>
</div>
</body>
</html>";
    }
}
