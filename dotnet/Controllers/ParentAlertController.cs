using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Historique consultable des alertes WinAI destinées à un parent (table ParentAlert).
/// Distinct de GET /api/parent/alerts (ParentController), qui lit l'ancienne table
/// Notification générique  legacy, non alimentée par le flux d'alertes actuel, non touchée ici
/// (ne jamais faire migrer ces alertes vers Notification ou DirectMessage).
/// </summary>
[ApiController]
[Route("api/parent-alerts")]
[Authorize]
public class ParentAlertController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ParentAlertController> _logger;

    public ParentAlertController(ApplicationDbContext db, ILogger<ParentAlertController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Liste des alertes d'un enfant, pour le parent connecté. Filtre optionnel is_read.</summary>
    [HttpGet("{childId:int}")]
    public async Task<IActionResult> GetAlerts(int childId, [FromQuery] bool? isRead)
    {
        var parentId = User.GetUserId();

        var linked = await _db.ParentStudentLinks
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { error = "Accès refusé : cet enfant n'est pas lié à votre compte." });

        var query = _db.ParentAlerts.AsNoTracking()
            .Where(a => a.ParentId == parentId && a.ChildId == childId);

        if (isRead.HasValue)
            query = query.Where(a => a.IsRead == isRead.Value);

        var rows = await query.OrderByDescending(a => a.DetectedAt).ToListAsync();
        var live = await LiveInactivityAsync(childId);
        var alerts = Freshen(rows, live).Select(x => new
        {
            id = x.Alert.Id,
            childId = x.Alert.ChildId,
            type = x.Alert.Type,
            severity = x.Alert.Severity,
            content = x.Content,
            isRead = x.Alert.IsRead,
            detectedAt = x.Alert.DetectedAt,
            createdAt = x.Alert.CreatedAt,
            inactivityDays = x.InactivityDays,
        });

        return Ok(alerts);
    }

    /// <summary>
    /// Baromètre de bien-être  vue consolidée des seuls signaux comportementaux
    /// (BaissePerformance, Inactivite, Surmenage, AnxieteExamen), 30 derniers jours.
    /// Ce n'est pas un nouveau moteur de détection : une lecture filtrée de
    /// ParentAlerts, déjà alimentée par parent_alert_routes.py côté Python à
    /// chaque calcul d'alerte (voir _persist_alerts). "Felicitations" est
    /// volontairement exclu : ce n'est pas un signal à surveiller.
    /// </summary>
    private static readonly string[] BarometreTypes = { "BaissePerformance", "Inactivite", "Surmenage", "AnxieteExamen" };

    [HttpGet("{childId:int}/barometre")]
    public async Task<IActionResult> GetBarometre(int childId)
    {
        var parentId = User.GetUserId();

        var linked = await _db.ParentStudentLinks
            .AnyAsync(l => l.ParentId == parentId && l.StudentId == childId && l.Status == "accepted");
        if (!linked)
            return StatusCode(403, new { error = "Accès refusé : cet enfant n'est pas lié à votre compte." });

        var cutoff = DateTime.UtcNow.AddDays(-30);

        var rows = await _db.ParentAlerts.AsNoTracking()
            .Where(a => a.ParentId == parentId && a.ChildId == childId
                && BarometreTypes.Contains(a.Type) && a.DetectedAt >= cutoff)
            .OrderByDescending(a => a.DetectedAt)
            .ToListAsync();
        var live = await LiveInactivityAsync(childId);

        // Baromètre = état actuel : un seul signal par type (le plus récent), pas l'historique.
        var signals = Freshen(rows, live)
            .GroupBy(x => x.Alert.Type == "InactiviteProlongee" ? "Inactivite" : x.Alert.Type)
            .Select(g => g.First())
            .Select(x => new
            {
                id = x.Alert.Id,
                type = x.Alert.Type,
                severity = x.Alert.Severity,
                message = x.Content,
                isRead = x.Alert.IsRead,
                detectedAt = x.Alert.DetectedAt,
                inactivityDays = x.InactivityDays,
            })
            .ToList();

        return Ok(new { signals });
    }

    // ── Inactivité vue au moment de la lecture (26/11/2026) ─────────────────────────────────
    // Les alertes d'inactivité sont enregistrées avec un texte figé (« depuis 14 jours ») : elles restaient
    // affichées, avec la même durée, après la reprise de l'enfant (quiz fait deux heures plus tôt). À la
    // lecture : une alerte d'inactivité est écartée si l'enfant a eu une activité après sa détection, et la
    // plus récente restante est réécrite avec la durée réelle ; les plus anciennes ne sont pas répétées.

    private static readonly HashSet<string> InactivityTypes = new() { "Inactivite", "InactiviteProlongee" };

    private sealed record LiveInactivity(DateTime? LastActivity, string FirstName);

    private sealed record FreshAlert(Backend.Models.Entities.ParentAlert Alert, string Content, int? InactivityDays);

    /// <summary>Dernière activité de l'enfant : quiz, leçon, séance de révision ou score du jour.</summary>
    private async Task<LiveInactivity> LiveInactivityAsync(int childId)
    {
        var quiz = await _db.QuizAttempts.AsNoTracking().Where(a => a.UserId == childId && a.CompletedAt != null)
            .MaxAsync(a => (DateTime?)a.CompletedAt);
        var lesson = await _db.LessonProgress.AsNoTracking().Where(p => p.UserId == childId)
            .MaxAsync(p => (DateTime?)p.UpdatedAt);
        var study = await _db.StudySessions.AsNoTracking().Where(s => s.UserId == childId)
            .MaxAsync(s => (DateTime?)(s.CompletedAt ?? s.CreatedAt));
        var daily = await _db.DailyScores.AsNoTracking().Where(d => d.UserId == childId)
            .MaxAsync(d => (DateTime?)d.UpdatedAt);
        var last = new[] { quiz, lesson, study, daily }.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty().Max();
        var name = await _db.Users.AsNoTracking().Where(u => u.Id == childId).Select(u => u.FirstName).FirstOrDefaultAsync();
        return new LiveInactivity(last == default ? null : last, string.IsNullOrWhiteSpace(name) ? "Votre enfant" : name!);
    }

    private static List<FreshAlert> Freshen(List<Backend.Models.Entities.ParentAlert> rows, LiveInactivity live)
    {
        var now = DateTime.UtcNow;
        var result = new List<FreshAlert>();
        var inactivityShown = false;
        foreach (var a in rows) // du plus récent au plus ancien
        {
            if (!InactivityTypes.Contains(a.Type))
            {
                result.Add(new FreshAlert(a, a.Content, null));
                continue;
            }
            if (live.LastActivity is DateTime last && last > a.DetectedAt) continue; // l'enfant a repris : résolue
            if (inactivityShown) continue;                                          // une seule, la plus récente
            inactivityShown = true;
            int? days = live.LastActivity is DateTime l ? (int)(now - l).TotalDays : null;
            var text = days is int d
                ? $"{live.FirstName} n'a pas eu d'activité sur WinPlus depuis {d} jour{(d > 1 ? "s" : "")}."
                : $"{live.FirstName} n'a pas encore eu d'activité sur WinPlus.";
            result.Add(new FreshAlert(a, text, days));
        }
        return result;
    }

    /// <summary>Marque une alerte comme lue.</summary>
    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var parentId = User.GetUserId();

        var alert = await _db.ParentAlerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert == null) return NotFound();
        if (alert.ParentId != parentId) return StatusCode(403, new { error = "Accès refusé." });

        if (!alert.IsRead)
        {
            alert.IsRead = true;
            await _db.SaveChangesAsync();
        }

        return Ok(new { id = alert.Id, isRead = alert.IsRead });
    }
}
