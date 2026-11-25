using System.Globalization;
using System.Text.Json;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Synthèses WinAI à la demande (25/11/2026), rédigées à partir de faits calculés ici (jamais de données
/// nominatives d'autres élèves envoyées au modèle) :
///   POST /api/parent/children/{childId}/bilan-semaine  bilan des 7 derniers jours d'un enfant lié, pour son
///        parent ; gardé dans l'historique des rapports (ParentReports, type « BilanSemaine ») ; débité sur le
///        budget WinAI du parent (inclus dans tous les plans).
///   POST /api/institution/groups/{id}/synthese          synthèse d'une classe de l'établissement (groupe créé
///        par le compte établissement) ; gardée dans WinAiProductions (« synthese-classe ») ; réservée aux
///        établissements sous licence et débitée sur le budget WinAI de la licence.
/// Réponse : {success, data:{id, title, text, createdAt, stats}} ; refus {success:false, error, code, message}.
/// </summary>
[ApiController]
[Authorize]
public class WinAiSynthesisController : ControllerBase
{
    public const string KindClass = "synthese-classe";
    private const string Endpoint = "/api/winai/synthese/generate-content";

    private readonly ApplicationDbContext _db;
    private readonly IFastApiClient _fastApi;
    private readonly ILogger<WinAiSynthesisController> _logger;

    public WinAiSynthesisController(ApplicationDbContext db, IFastApiClient fastApi, ILogger<WinAiSynthesisController> logger)
    {
        _db = db; _fastApi = fastApi; _logger = logger;
    }

    private static ObjectResult Refus(int status, string code, string message) =>
        new(new { success = false, error = message, code, message }) { StatusCode = status };

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static string Pct(double v) => Math.Round(v).ToString(Fr) + " %";

    // ── Parent : bilan de la semaine d'un enfant ──────────────────────────────

    [HttpPost("api/parent/children/{childId:int}/bilan-semaine")]
    public async Task<IActionResult> BilanSemaine(int childId, [FromServices] IAiFeatureGate gate, CancellationToken ct)
    {
        var parentId = User.GetUserId();
        var linked = await _db.ParentStudentLinks.AsNoTracking()
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted", ct);
        if (!linked) return Refus(403, "enfant_non_lie", "Cet enfant n'est pas lié à votre compte.");

        var child = await _db.Users.AsNoTracking().Where(u => u.Id == childId)
            .Select(u => new { u.FirstName, u.Level }).FirstOrDefaultAsync(ct);
        var name = string.IsNullOrWhiteSpace(child?.FirstName) ? "votre enfant" : child!.FirstName!.Trim();

        var now = DateTime.UtcNow;
        var start = now.AddDays(-7);
        var prevStart = now.AddDays(-14);
        var attempts = await (from a in _db.QuizAttempts.AsNoTracking()
                              join q in _db.Quizzes.AsNoTracking() on a.QuizId equals q.Id
                              where a.UserId == childId && a.IsCompleted && a.CompletedAt >= prevStart
                              select new { a.CompletedAt, Score = (double)a.Score, q.Subject, a.TimeSpentSeconds })
            .ToListAsync(ct);
        var week = attempts.Where(a => a.CompletedAt >= start).ToList();
        var prev = attempts.Where(a => a.CompletedAt < start).ToList();
        double? weekAvg = week.Count > 0 ? Math.Round(week.Average(a => a.Score), 1) : null;
        double? prevAvg = prev.Count > 0 ? Math.Round(prev.Average(a => a.Score), 1) : null;
        var activeDays = week.Select(a => a.CompletedAt.Date).Distinct().Count();
        var minutes = (int)Math.Round(week.Sum(a => (a.TimeSpentSeconds ?? 0)) / 60.0);
        var subjects = week.GroupBy(a => string.IsNullOrWhiteSpace(a.Subject) ? "Autre" : a.Subject)
            .Select(g => new { name = g.Key, quizCount = g.Count(), average = Math.Round(g.Average(a => a.Score), 1) })
            .OrderByDescending(s => s.quizCount).ToList();
        var downloads = await _db.DownloadHistories.AsNoTracking().CountAsync(d => d.UserId == childId && d.CreatedAt >= start, ct);
        var goals = await _db.Goals.AsNoTracking().Where(g => g.UserId == childId && g.Status == GoalStatus.Active)
            .OrderBy(g => g.TargetDate).Select(g => g.Title).Take(3).ToListAsync(ct);

        var facts = new List<string>
        {
            $"Prénom : {name}" + (string.IsNullOrWhiteSpace(child?.Level) ? "" : $", classe : {child!.Level}"),
            week.Count == 0 ? "Aucun quiz terminé ces 7 derniers jours." : $"{week.Count} quiz terminé(s) ces 7 derniers jours, sur {activeDays} jour(s) différent(s).",
        };
        if (weekAvg != null) facts.Add($"Moyenne des quiz de la semaine : {Pct(weekAvg.Value)}.");
        if (prevAvg != null) facts.Add($"Moyenne de la semaine précédente : {Pct(prevAvg.Value)} ({prev.Count} quiz).");
        if (minutes > 0) facts.Add($"Temps passé sur les quiz : environ {minutes} minutes.");
        foreach (var s in subjects.Take(6)) facts.Add($"{s.name} : {s.quizCount} quiz, moyenne {Pct(s.average)}.");
        if (downloads > 0) facts.Add($"{downloads} épreuve(s) ou document(s) consulté(s).");
        if (goals.Count > 0) facts.Add($"Objectifs en cours : {string.Join(" ; ", goals.Where(g => !string.IsNullOrWhiteSpace(g)))}.");

        var ticket = await gate.BeginIncludedAsync(parentId, "bilan_semaine", ct);
        if (!ticket.Allowed) return ticket.Refusal!;
        var (status, title, text) = await RedigerAsync("parent-week", name, facts, () => gate.CompleteAsync(ticket, served: true), () => gate.CompleteAsync(ticket, served: false));
        if (status != 200) return Refus(502, "generation_echouee", "WinAI n'a pas pu rédiger le bilan. Réessayez dans un instant.");

        var report = new ParentReport
        {
            ParentId = parentId, ChildId = childId, ReportType = "BilanSemaine", Content = text,
            CapsuleText = title, EmitterType = "WinAI",
        };
        _db.ParentReports.Add(report);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, new
        {
            success = true,
            data = new
            {
                id = report.Id, title, text, createdAt = report.CreatedAt, childId,
                periodStart = start, periodEnd = now,
                stats = new
                {
                    quizCount = week.Count, activeDays, minutes, weekAvg, prevAvg,
                    delta = weekAvg != null && prevAvg != null ? Math.Round(weekAvg.Value - prevAvg.Value, 1) : (double?)null,
                    subjects, downloads,
                },
            },
        });
    }

    // ── Établissement : synthèse d'une classe ─────────────────────────────────

    [HttpPost("api/institution/groups/{id:int}/synthese")]
    public async Task<IActionResult> SyntheseClasse(int id, [FromServices] InstitutionLicenseService licenses, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var me = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Role, u.InstitutionId }).FirstOrDefaultAsync(ct);
        if (me == null || !string.Equals(me.Role, "institution", StringComparison.OrdinalIgnoreCase) || me.InstitutionId is not int institutionId)
            return Refus(403, "reserve_etablissement", "La synthèse de classe est réservée aux comptes établissement.");
        var group = await _db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId && g.IsActive, ct);
        if (group == null) return Refus(404, "introuvable", "Classe introuvable.");

        var (budget, used) = await licenses.AiBudgetAsync(institutionId, ct);
        if (budget <= 0)
            return Refus(403, "license_required", "Les analyses WinAI de l'établissement sont incluses dans la licence WinPlus. Souscrivez une licence (ou un essai) pour les utiliser.");
        if (used >= budget)
            return Refus(402, "institution_ai_budget_exhausted", "Le budget WinAI de votre licence est épuisé pour cette période.");

        var memberIds = await _db.StudyGroupMembers.AsNoTracking()
            .Where(m => m.StudyGroupId == id && m.UserId != userId && m.Role != "teacher").Select(m => m.UserId).ToListAsync(ct);
        if (memberIds.Count == 0) return Refus(400, "classe_vide", "Cette classe n'a pas encore d'élève : ajoutez-en pour obtenir une synthèse.");

        var now = DateTime.UtcNow;
        var since = now.AddDays(-30);
        var weekStart = now.AddDays(-7);
        var attempts = await (from a in _db.QuizAttempts.AsNoTracking()
                              join q in _db.Quizzes.AsNoTracking() on a.QuizId equals q.Id
                              where memberIds.Contains(a.UserId) && a.IsCompleted && a.CompletedAt >= since
                              select new { a.UserId, a.CompletedAt, Score = (double)a.Score, q.Subject })
            .ToListAsync(ct);
        var byStudent = attempts.GroupBy(a => a.UserId).Select(g => g.Average(a => a.Score)).ToList();
        var activeWeek = attempts.Where(a => a.CompletedAt >= weekStart).Select(a => a.UserId).Distinct().Count();
        double? classAvg = attempts.Count > 0 ? Math.Round(attempts.Average(a => a.Score), 1) : null;
        var below50 = byStudent.Count(s => s < 50);
        var subjects = attempts.GroupBy(a => string.IsNullOrWhiteSpace(a.Subject) ? "Autre" : a.Subject)
            .Select(g => new { name = g.Key, quizCount = g.Count(), students = g.Select(a => a.UserId).Distinct().Count(), average = Math.Round(g.Average(a => a.Score), 1) })
            .OrderByDescending(s => s.quizCount).ToList();

        var facts = new List<string>
        {
            $"Classe : {group.Name}" + (string.IsNullOrWhiteSpace(group.Subject) ? "" : $", niveau {group.Subject}") + $", {memberIds.Count} élève(s).",
            $"{byStudent.Count} élève(s) ont fait au moins un quiz ces 30 derniers jours, {activeWeek} cette semaine.",
            $"{attempts.Count} quiz terminés en 30 jours.",
        };
        if (classAvg != null) facts.Add($"Moyenne de la classe aux quiz sur 30 jours : {Pct(classAvg.Value)}.");
        if (byStudent.Count > 0) facts.Add($"{below50} élève(s) sous 50 % de moyenne.");
        foreach (var s in subjects.Take(8)) facts.Add($"{s.name} : {s.quizCount} quiz par {s.students} élève(s), moyenne {Pct(s.average)}.");

        var (status, title, text) = await RedigerAsync("class", group.Name, facts, () => Task.CompletedTask, () => Task.CompletedTask);
        await licenses.RecordAiUsageAsync(userId, "synthese", _fastApi.ConsumeWinAiTokens(), ct);
        if (status != 200) return Refus(502, "generation_echouee", "WinAI n'a pas pu rédiger la synthèse. Réessayez dans un instant.");

        var stats = new
        {
            students = memberIds.Count, activeStudents30d = byStudent.Count, activeStudents7d = activeWeek,
            quizCount30d = attempts.Count, classAvg, studentsBelow50 = below50, subjects,
        };
        var production = new WinAiProduction
        {
            UserId = userId, Kind = KindClass, Title = title, Subject = group.Name.Length > 150 ? group.Name[..150] : group.Name,
            Level = group.Subject, ItemCount = memberIds.Count,
            ContentJson = JsonSerializer.Serialize(new { text, stats, groupId = id }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        _db.WinAiProductions.Add(production);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, new
        {
            success = true,
            data = new { id = production.Id, title, text, createdAt = production.CreatedAt, groupId = id, periodStart = since, periodEnd = now, stats },
        });
    }

    /// <summary>Appel au service Python ; clôt le débit (servi ou non) avant de rendre la main.</summary>
    private async Task<(int Status, string Title, string Text)> RedigerAsync(string kind, string name, List<string> facts,
        Func<Task> onServed, Func<Task> onFailed)
    {
        int status = 0; string? raw = null;
        try
        {
            (status, raw) = await _fastApi.PostRawJsonAsync(Endpoint, new { kind, name, facts });
        }
        finally
        {
            if (status == 200) await onServed(); else await onFailed();
        }
        if (status != 200 || raw == null)
        {
            _logger.LogWarning("Synthèse WinAI {Kind} : HTTP {Status} {Body}", kind, status, raw?.Length > 300 ? raw[..300] : raw);
            return (status == 200 ? 502 : status, "", "");
        }
        using var doc = JsonDocument.Parse(raw);
        var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var text = doc.RootElement.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "";
        return string.IsNullOrWhiteSpace(text) ? (502, "", "") : (200, title, text);
    }
}
