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
/// de pilotage aux institutions (Role = "institution") actives.
///
/// ⚠ Correction §7.5 du suivi : ce service cherchait <c>Role == "organization"</c>.
/// Deux valeurs de rôle coexistaient pour le même type de compte, et le rôle
/// officiel retenu est <c>"institution"</c> c'est celui que produit
/// l'inscription, celui que teste le frontend web (RoleAwareShell) et celui que
/// connaît l'administration. Le rapport mensuel ne trouvait donc aucune
/// institution et ne partait jamais.
/// </summary>
public sealed class MonthlyInstitutionReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyInstitutionReportService> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceTokenProvider _serviceToken;

    public MonthlyInstitutionReportService(
        IServiceScopeFactory scopeFactory,
        ILogger<MonthlyInstitutionReportService> logger,
        IHttpClientFactory httpFactory,
        IServiceTokenProvider serviceToken)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpFactory = httpFactory;
        _serviceToken = serviceToken;
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

    /// <summary>
    /// Envoi à la demande depuis l'espace institution : le mois en cours
    /// jusqu'à aujourd'hui. Renvoie false si le compte n'a pas d'e-mail vérifié.
    /// </summary>
    public async Task<bool> SendNowAsync(int institutionUserId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var inst = await db.Users.FirstOrDefaultAsync(u => u.Id == institutionUserId, ct);
        if (inst == null || string.IsNullOrWhiteSpace(inst.Email)) return false;
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        await SendMonthlyReportAsync(inst.Id, inst.Email, inst.FirstName ?? "Directeur", db, email, ct, start, now);
        return true;
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
        var preferences = scope.ServiceProvider.GetRequiredService<INotificationPreferenceService>();

        // Find all active institution users (Role = "institution", §7.5)
        var institutions = await db.Users
            .Where(u => u.Role.ToLower() == "institution"
                     && u.IsActive && !u.IsDeleted && u.IsEmailVerified)
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} institution users for monthly report.", institutions.Count);

        foreach (var inst in institutions)
        {
            if (ct.IsCancellationRequested) break;
            // Module 22 : e-mail non transactionnel, soumis à la préférence
            // « Notifications par e-mail » (vérifiée avant l'appel WinAI).
            // Partie 12.3 : la règle fait foi dans EmailService.SendGenericEmailAsync ;
            // ce pré-contrôle est conservé pour ne pas payer l'appel WinAI d'un
            // établissement qui ne recevra pas l'e-mail.
            if (!await preferences.AllowsAsync(inst.Id, NotificationChannel.Email, NotificationCategory.General, ct)) continue;
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
        CancellationToken ct,
        DateTime? periodStart = null,
        DateTime? periodEnd = null)
    {
        var now = DateTime.UtcNow;
        var monthStart = periodStart ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);
        var monthEnd = periodEnd ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthLabel = monthStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));
        if (periodEnd != null) monthLabel += $" (au {monthEnd:dd/MM})";

        // Établissement du compte (User.InstitutionId) et ses élèves : ceux de
        // InstitutionStudents ayant accepté le rattachement. L'ancienne requête
        // comparait User.InstitutionId des élèves à l'id du COMPTE institution
        // (et non de l'établissement) : aucun élève n'était jamais trouvé.
        var institutionId = await db.Users.Where(u => u.Id == institutionUserId)
            .Select(u => u.InstitutionId).FirstOrDefaultAsync(ct);
        var institutionName = institutionId == null ? directorName
            : await db.Institutions.Where(i => i.Id == institutionId).Select(i => i.Name).FirstOrDefaultAsync(ct) ?? directorName;
        var studentIds = institutionId == null ? new List<int>() : await db.InstitutionStudents.AsNoTracking()
            .Where(s => s.InstitutionId == institutionId && s.IsActive
                        && s.ConfirmationStatus == Backend.Models.Entities.InstitutionAttachmentStatuses.Accepted)
            .Select(s => s.StudentId).Distinct().ToListAsync(ct);

        // Chiffres du mois, à partir des quiz terminés.
        var attempts = await db.QuizAttempts.AsNoTracking()
            .Where(a => studentIds.Contains(a.UserId) && a.IsCompleted && a.CompletedAt >= monthStart && a.CompletedAt < monthEnd)
            .Select(a => new { a.UserId, a.Score })
            .ToListAsync(ct);
        var avgScore = attempts.Count > 0 ? Math.Round(attempts.Average(a => (double)a.Score), 1) : 0.0;
        var activeStudents = attempts.Select(a => a.UserId).Distinct().Count();
        var passRate = studentIds.Count > 0
            ? Math.Round(attempts.GroupBy(a => a.UserId).Count(g => g.Average(a => (double)a.Score) >= 50) * 100.0 / studentIds.Count, 1)
            : 0.0;
        var totalQuizzes = attempts.Count;

        // Activité de l'établissement sur le mois (phase 2).
        int homeworkGiven = 0, announcementsSent = 0, bulletinsShared = 0;
        double homeworkDoneRate = 0;
        if (institutionId != null)
        {
            var homework = await db.InstitutionHomework.AsNoTracking()
                .Where(h => h.InstitutionId == institutionId && !h.IsDeleted && h.CreatedAt >= monthStart && h.CreatedAt < monthEnd)
                .Select(h => new { h.QuizId, h.CreatedAt, h.StudyGroupId }).ToListAsync(ct);
            homeworkGiven = homework.Count;
            if (homework.Count > 0)
            {
                int expected = 0, done = 0;
                foreach (var h in homework)
                {
                    var members = await db.StudyGroupMembers.AsNoTracking()
                        .Where(m => m.StudyGroupId == h.StudyGroupId && m.Role != "teacher").Select(m => m.UserId).ToListAsync(ct);
                    expected += members.Count;
                    done += await db.QuizAttempts.AsNoTracking()
                        .Where(a => a.QuizId == h.QuizId && a.IsCompleted && a.CompletedAt >= h.CreatedAt && members.Contains(a.UserId))
                        .Select(a => a.UserId).Distinct().CountAsync(ct);
                }
                homeworkDoneRate = expected == 0 ? 0 : Math.Round(done * 100.0 / expected, 1);
            }
            announcementsSent = await db.InstitutionAnnouncements.CountAsync(a => a.InstitutionId == institutionId && !a.IsDeleted && a.CreatedAt >= monthStart && a.CreatedAt < monthEnd, ct);
            bulletinsShared = await db.InstitutionBulletins.CountAsync(b => b.InstitutionId == institutionId && !b.IsDeleted && b.SharedAt >= monthStart && b.SharedAt < monthEnd, ct);
        }
        var inactiveStudents = Math.Max(0, studentIds.Count - activeStudents);

        // Build AI narrative via Python (optional  fire-and-forget)
        string aiNarrative;
        // Sans élève rattaché, Python retomberait sur « tous les élèves de la
        // plateforme » (repli de test de _resolve_student_ids) : on n'appelle pas.
        if (studentIds.Count == 0)
        {
            aiNarrative = "Aucun élève rattaché à votre établissement ce mois-ci : pas de plan d'action WinAI.";
        }
        else
        try
        {
            var pyClient = _httpFactory.CreateClient("FastApiClient");
            var payload = new
            {
                institution_id = institutionId ?? institutionUserId,
                student_ids = studentIds,
                institution_name = institutionName,
            };
            // Module 23 : tâche de fond sans jeton utilisateur. L'appel partait
            // sans Authorization, Python (require_role) répondait 401, et le
            // catch muet ci-dessous remplaçait l'analyse par un texte générique.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/institution/action-plan")
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.TryAddWithoutValidation("Authorization", _serviceToken.CreateAuthorizationHeader(ServiceScopes.InstitutionReport));
            var response = await pyClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                var lines = new List<string>();
                if (json.TryGetProperty("actions", out var a) && a.ValueKind == JsonValueKind.Array)
                {
                    foreach (var action in a.EnumerateArray())
                    {
                        var text = action.TryGetProperty("action", out var t) ? t.GetString() : null;
                        var effort = action.TryGetProperty("effort", out var e) ? e.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(text))
                            lines.Add($"{lines.Count + 1}. {text}" + (string.IsNullOrWhiteSpace(effort) ? "" : $" ({effort})"));
                    }
                }
                aiNarrative = lines.Count > 0
                    ? $"WinAI recommande pour {monthLabel} :\n{string.Join("\n", lines)}"
                    : "Analyse WinAI indisponible pour ce rapport.";
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    _logger.LogError("Rapport institution : authentification service à service refusée par Python ({Status}) : {Body}", (int)response.StatusCode, errorBody);
                else
                    _logger.LogWarning("Rapport institution : plan d'action WinAI en échec ({Status}) : {Body}", (int)response.StatusCode, errorBody);
                aiNarrative = "Analyse WinAI indisponible pour ce rapport.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Rapport institution : appel du plan d'action WinAI impossible pour {InstitutionUserId}", institutionUserId);
            aiNarrative = "Analyse WinAI indisponible pour ce rapport.";
        }

        // Build HTML email body
        var body = BuildReportHtml(
            directorName, monthLabel, studentIds.Count,
            activeStudents, avgScore, passRate, totalQuizzes, aiNarrative,
            institutionName, homeworkGiven, homeworkDoneRate, announcementsSent, bulletinsShared, inactiveStudents);

        await emailService.SendGenericEmailAsync(
            to: institutionEmail,
            subject: $"[WinPlus] Rapport mensuel de pilotage  {monthLabel}",
            htmlContent: body,
            category: NotificationCategory.General);

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
        string aiNarrative,
        string institutionName,
        int homeworkGiven,
        double homeworkDoneRate,
        int announcementsSent,
        int bulletinsShared,
        int inactiveStudents)
    {
        var engagementRate = totalStudents > 0
            ? Math.Round((double)activeStudents / totalStudents * 100, 1) : 0;
        var aiNarrativeHtml = aiNarrative.Replace("\n", "<br/>");
        var inactiveLine = inactiveStudents > 0
            ? $"<strong>{inactiveStudents}</strong> élève(s) n'ont fait aucun quiz sur la période : pensez à les relancer."
            : "Tous vos élèves ont été actifs sur la période.";

        return $@"<!DOCTYPE html>
<html lang=""fr"">
<head>
  <meta charset=""utf-8""/>
  <style>
    body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #f8fafc; margin: 0; padding: 0; }}
    .wrap {{ max-width: 600px; margin: 0 auto; background: #fff; border-radius: 12px; overflow: hidden; }}
    .header {{ background: #0F2A35; padding: 28px 32px; }}
    .header h1 {{ color: #fff; font-size: 22px; margin: 0 0 4px; }}
    .header p {{ color: #86D8CF; font-size: 14px; margin: 0; }}
    .body {{ padding: 28px 32px; }}
    .kpi-row {{ display: flex; gap: 12px; margin-bottom: 20px; flex-wrap: wrap; }}
    .kpi {{ flex: 1; min-width: 120px; background: #EBF8F6; border: 1px solid #ADE3DC;
             border-radius: 8px; padding: 14px 16px; text-align: center; }}
    .kpi .value {{ font-size: 26px; font-weight: 800; color: #1E8077; line-height: 1; }}
    .kpi .label {{ font-size: 11px; color: #6b7280; margin-top: 4px; }}
    .section {{ margin-bottom: 20px; }}
    .section h3 {{ font-size: 15px; font-weight: 700; color: #1f2937; margin: 0 0 10px; }}
    .ai-box {{ background: #0F2A35; border-radius: 8px; padding: 16px 20px; color: #e2e8f0; font-size: 13px; line-height: 1.6; }}
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
    <p style=""font-size:13px;color:#6b7280;margin-top:4px"">Voici le bilan de pilotage pédagogique de <strong>{System.Net.WebUtility.HtmlEncode(institutionName)}</strong> pour {monthLabel}.</p>

    <div class=""kpi-row"">
      <div class=""kpi""><div class=""value"">{totalStudents}</div><div class=""label"">Étudiants inscrits</div></div>
      <div class=""kpi""><div class=""value"">{activeStudents}</div><div class=""label"">Actifs ce mois</div></div>
      <div class=""kpi""><div class=""value"">{avgScore}%</div><div class=""label"">Score moyen</div></div>
      <div class=""kpi""><div class=""value"">{passRate}%</div><div class=""label"">Taux de réussite</div></div>
      <div class=""kpi""><div class=""value"">{engagementRate}%</div><div class=""label"">Engagement</div></div>
      <div class=""kpi""><div class=""value"">{totalQuizzes}</div><div class=""label"">Quiz complétés</div></div>
    </div>

    <div class=""section"">
      <h3>Vie de l'établissement</h3>
      <div class=""kpi-row"">
        <div class=""kpi""><div class=""value"">{homeworkGiven}</div><div class=""label"">Devoirs donnés</div></div>
        <div class=""kpi""><div class=""value"">{homeworkDoneRate}%</div><div class=""label"">Devoirs faits</div></div>
        <div class=""kpi""><div class=""value"">{announcementsSent}</div><div class=""label"">Annonces</div></div>
        <div class=""kpi""><div class=""value"">{bulletinsShared}</div><div class=""label"">Bulletins partagés</div></div>
      </div>
      <p style=""font-size:13px;color:#374151;margin:0"">{inactiveLine}</p>
    </div>

    <div class=""section"">
      <h3>Plan d'action WinAI</h3>
      <div class=""ai-box"">{aiNarrativeHtml}</div>
    </div>

    <p style=""font-size:12px;color:#9ca3af"">
      Pour consulter l'analyse complète, connectez-vous à votre espace institution sur
      <a href=""https://winplus.cm/organisation"" style=""color:#1E8077"">WinPlus</a>.
    </p>
  </div>
  <div class=""footer"">
    WinPlus · Plateforme éducative · <a href=""https://winplus.cm"" style=""color:#1E8077"">winplus.cm</a>
    <br/>Ce rapport est généré automatiquement le 1er de chaque mois.
  </div>
</div>
</body>
</html>";
    }
}