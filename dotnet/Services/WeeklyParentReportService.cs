using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
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
/// Service hébergé qui envoie chaque lundi à 8h UTC un rapport hebdomadaire
/// aux parents qui ont activé la fonctionnalité (Bio contient weeklyReport:true).
/// </summary>
public sealed class WeeklyParentReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeeklyParentReportService> _logger;
    private readonly IHttpClientFactory _httpFactory;

    public WeeklyParentReportService(
        IServiceScopeFactory scopeFactory,
        ILogger<WeeklyParentReportService> logger,
        IHttpClientFactory httpFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WeeklyParentReportService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextMonday();
            _logger.LogInformation("Next weekly report run in {Hours}h {Minutes}m.", (int)delay.TotalHours, delay.Minutes);

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
                _logger.LogError(ex, "WeeklyParentReportService run failed.");
            }
        }
    }

    private static TimeSpan ComputeDelayUntilNextMonday()
    {
        var now = DateTime.UtcNow;
        // Next Monday at 08:00 UTC
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && now.Hour >= 8) daysUntilMonday = 7;
        var next = now.Date.AddDays(daysUntilMonday).AddHours(8);
        return next - now;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Running WeeklyParentReport at {Time}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        // Find all parents with weeklyReport enabled in their Bio JSON
        var parents = await db.Users
            .Where(u => u.Role == "parent" && u.IsActive && !u.IsDeleted && u.IsEmailVerified)
            .ToListAsync(ct);

        foreach (var parent in parents)
        {
            if (ct.IsCancellationRequested) break;

            // Check if weekly report is enabled in Bio settings
            if (!IsWeeklyReportEnabled(parent.Bio)) continue;

            try
            {
                await SendWeeklyReportAsync(parent.Email, parent.FirstName ?? "Parent", db, email, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send weekly report to {Email}", parent.Email);
            }
        }
    }

    private static bool IsWeeklyReportEnabled(string? bio)
    {
        if (string.IsNullOrWhiteSpace(bio) || !bio.TrimStart().StartsWith("{")) return false;
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(bio);
            return doc.TryGetProperty("weeklyReport", out var val) && val.GetBoolean();
        }
        catch { return false; }
    }

    private async Task SendWeeklyReportAsync(
        string parentEmail,
        string parentFirstName,
        ApplicationDbContext db,
        IEmailService emailService,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);

        // Find children linked to this parent
        var parentUser = await db.Users
            .FirstOrDefaultAsync(u => u.Email == parentEmail && !u.IsDeleted, ct);
        if (parentUser == null) return;

        var children = await db.ParentStudentLinks
            .Where(l => l.ParentId == parentUser.Id)
            .Include(l => l.Student)
            .Select(l => l.Student)
            .Where(s => s != null && !s.IsDeleted)
            .ToListAsync(ct);

        if (!children.Any())
        {
            _logger.LogInformation("Parent {Email} has no linked children  skipping report.", parentEmail);
            return;
        }

        // Build per-child summary
        var childSummaries = new List<string>();
        foreach (var child in children)
        {
            if (child == null) continue;

            var scores = await db.DailyScores
                .Where(s => s.UserId == child.Id && s.CreatedAt >= cutoff)
                .ToListAsync(ct);

            var weekAvg = scores.Any() ? scores.Average(s => (double)s.AverageScore) : 0.0;
            var prevScores = await db.DailyScores
                .Where(s => s.UserId == child.Id && s.CreatedAt >= cutoff.AddDays(-7) && s.CreatedAt < cutoff)
                .ToListAsync(ct);
            var prevAvg = prevScores.Any() ? prevScores.Average(s => (double)s.AverageScore) : 0.0;
            var delta = weekAvg - prevAvg;

            var quizCount = await db.QuizAttempts
                .CountAsync(a => a.UserId == child.Id && a.CompletedAt >= cutoff, ct);

            var trend = delta > 2 ? "en progression" : delta < -2 ? "en baisse" : "stable";
            childSummaries.Add(
                $"{child.FirstName ?? "Votre enfant"} : score moyen {weekAvg:F0}% ({trend}), {quizCount} exercice(s) cette semaine."
            );
        }

        // Generate synthesis via Python AI service
        var synthesis = await GenerateSynthesisAsync(childSummaries, parentFirstName, ct);

        // Build HTML email
        var childRows = string.Join("", childSummaries.Select(s =>
            $"<tr><td style=\"padding:10px 0;font-family:-apple-system,Arial,sans-serif;font-size:14px;color:#4E7280;border-bottom:1px solid #E8E0CE;\">{System.Net.WebUtility.HtmlEncode(s)}</td></tr>"
        ));

        var html = $@"<!DOCTYPE html>
<html lang=""fr"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>Rapport hebdomadaire WinPlus</title></head>
<body style=""margin:0;padding:0;background:#F6F0E4;"">
<table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:#F6F0E4;"">
<tr><td align=""center"" style=""padding:40px 16px;"">
<table width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""max-width:600px;"">
  <tr><td align=""center"" style=""padding:0 0 24px;"">
    <span style=""font-family:'Bricolage Grotesque',-apple-system,Arial,sans-serif;font-size:22px;font-weight:700;color:#0F2A35;"">Win<em style=""color:#259A8E;"">+</em></span>
  </td></tr>
  <tr><td style=""background:#fff;border-radius:20px;padding:40px 36px;"">
    <p style=""margin:0 0 8px;font-family:Arial,sans-serif;font-size:12px;font-weight:700;letter-spacing:0.09em;color:#259A8E;text-transform:uppercase;"">Rapport hebdomadaire</p>
    <h1 style=""margin:0 0 24px;font-family:-apple-system,Arial,sans-serif;font-size:22px;color:#0F2A35;"">Bonjour {System.Net.WebUtility.HtmlEncode(parentFirstName)}  voici la semaine de vos enfants</h1>
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin:0 0 28px;"">
      {childRows}
    </table>
    <div style=""background:#EBF8F6;border:1px solid #D6F1ED;border-radius:14px;padding:18px 20px;margin:0 0 28px;"">
      <p style=""margin:0 0 6px;font-family:Arial,sans-serif;font-size:12px;font-weight:700;color:#1E8077;text-transform:uppercase;letter-spacing:0.08em;"">Synthèse WinAI</p>
      <p style=""margin:0;font-family:-apple-system,Arial,sans-serif;font-size:14px;line-height:1.6;color:#1F4A5A;"">{System.Net.WebUtility.HtmlEncode(synthesis)}</p>
    </div>
    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
      <tr><td align=""center"">
        <a href=""https://winplus.cm/parent"" style=""display:inline-block;padding:14px 32px;background:#0F2A35;color:#6BCFC6;font-family:-apple-system,Arial,sans-serif;font-size:14px;font-weight:700;border-radius:10px;text-decoration:none;"">Voir le tableau de bord →</a>
      </td></tr>
    </table>
  </td></tr>
  <tr><td align=""center"" style=""padding:24px 0 0;"">
    <p style=""margin:0;font-family:-apple-system,Arial,sans-serif;font-size:12px;color:#97AAB2;"">
      Vous recevez ce rapport car vous l'avez activé dans vos paramètres.
      <a href=""https://winplus.cm/parent"" style=""color:#97AAB2;"">Désactiver</a>
    </p>
  </td></tr>
</table>
</td></tr>
</table>
</body></html>";

        await emailService.SendGenericEmailAsync(
            parentEmail,
            $"Rapport hebdomadaire WinPlus  semaine du {cutoff:dd/MM}",
            html
        );

        _logger.LogInformation("Weekly report sent to {Email} ({Children} children).", parentEmail, children.Count);
    }

    private async Task<string> GenerateSynthesisAsync(List<string> childSummaries, string parentName, CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("FastApiClient");
            var prompt = new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = $"Résumé hebdomadaire pour le parent {parentName} : {string.Join(" | ", childSummaries)}. "
                                + "Rédige un bilan en 3 courtes phrases : (1) point positif de la semaine, "
                                + "(2) point d'attention s'il y en a un, (3) suggestion concrète pour soutenir les enfants."
                    }
                },
                system_prompt = "Tu es WinAI, conseiller pédagogique familial bienveillant de WinPlus. Réponds en français, en 3 phrases courtes et positives.",
                max_tokens = 150,
                temperature = 0.7
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/chatbot/chat");
            req.Content = JsonContent.Create(prompt);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var res = await client.SendAsync(req, cts.Token);
            var body = await res.Content.ReadAsStringAsync(cts.Token);
            var doc = JsonSerializer.Deserialize<JsonElement>(body);
            if (doc.TryGetProperty("content", out var contentProp))
                return contentProp.GetString() ?? FallbackSynthesis(childSummaries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI synthesis generation failed, using fallback.");
        }

        return FallbackSynthesis(childSummaries);
    }

    private static string FallbackSynthesis(List<string> summaries)
        => summaries.Any()
            ? "Vos enfants ont été actifs cette semaine sur WinPlus. Continuez à les encourager  chaque effort compte ! Un message de votre part peut faire toute la différence dans leur motivation."
            : "Aucune activité enregistrée cette semaine. Invitez vos enfants à démarrer une session de révision dès aujourd'hui.";
}
