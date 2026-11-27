using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Services;
using Backend.Models.Entities;

namespace Backend.Controllers;

public record CreateGroupRequest(string Name, string Level, string? Description);
public record AddGroupMemberRequest(string Email);

/// <summary>
/// Endpoints institution côté mobile.
/// Route: /api/institution  (distinct de /api/institutions qui gère l'annuaire global)
///
/// Les groupes d'une institution sont des StudyGroups créés par l'utilisateur institution.
/// Ses élèves sont les InstitutionStudents liés à son InstitutionId.
/// </summary>
[ApiController]
[Route("api/institution")]
[Authorize]
public class InstitutionMobileController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionMobileController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public InstitutionMobileController(ApplicationDbContext db,
        ILogger<InstitutionMobileController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    // ── Groupes ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/institution/groups  liste les groupes de l'institution.</summary>
    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        try
        {
            var userId = User.GetUserId();
            var groups = await _db.StudyGroups
                .AsNoTracking()
                .Where(g => g.OwnerId == userId && g.IsActive)
                .Select(g => new
                {
                    id = g.Id,
                    name = g.Name,
                    description = g.Description,
                    level = g.Subject ?? "",
                    // Les enseignants affectés (rôle « teacher ») ne sont pas des élèves.
                    memberCount = g.Members.Count(m => m.UserId != userId && m.Role != "teacher"),
                    teachers = g.Members.Where(m => m.Role == "teacher" && m.User != null)
                        .Select(m => new { id = m.UserId, firstName = m.User!.FirstName, lastName = m.User.LastName })
                        .ToList(),
                })
                .ToListAsync();

            return Ok(groups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution groups");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/institution/groups  créer un groupe.</summary>
    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var joinCode = Guid.NewGuid().ToString("N")[..6].ToUpper();

            var group = new StudyGroup
            {
                OwnerId = userId,
                Name = req.Name,
                Subject = req.Level,
                Description = req.Description,
                JoinCode = joinCode,
                IsActive = true,
            };
            _db.StudyGroups.Add(group);
            await _db.SaveChangesAsync();

            return Ok(new { id = group.Id, joinCode = group.JoinCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating group");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>DELETE /api/institution/groups/{id}  supprimer un groupe.</summary>
    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            group.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting group");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>GET /api/institution/groups/{id}/members  membres d'un groupe.</summary>
    [HttpGet("groups/{id:int}/members")]
    public async Task<IActionResult> GetGroupMembers([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            var rows = await _db.StudyGroupMembers
                .AsNoTracking()
                .Where(m => m.StudyGroupId == id && m.UserId != userId && m.Role != "teacher")
                .Select(m => new
                {
                    id = m.UserId,
                    firstName = m.User != null ? m.User.FirstName : null,
                    lastName = m.User != null ? m.User.LastName : null,
                    email = m.User != null ? m.User.Email : null,
                    lastLoginAt = m.User != null ? m.User.LastLoginAt : null,
                })
                .ToListAsync();

            // Moyenne réelle des quiz terminés et nombre de quiz des 7 derniers
            // jours (l'ancienne réponse renvoyait 0 en dur pour tout le monde).
            var memberIds = rows.Select(r => r.id).ToList();
            var weekAgo = DateTime.UtcNow.AddDays(-7);
            var attempts = await _db.QuizAttempts.AsNoTracking()
                .Where(a => memberIds.Contains(a.UserId) && a.IsCompleted)
                .Select(a => new { a.UserId, a.Score, a.CompletedAt })
                .ToListAsync();
            var byUser = attempts.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.ToList());

            var members = rows.Select(r =>
            {
                byUser.TryGetValue(r.id, out var list);
                return new
                {
                    r.id, r.firstName, r.lastName, r.email, r.lastLoginAt,
                    averageScore = list == null || list.Count == 0 ? (double?)null : Math.Round((double)list.Average(a => a.Score), 1),
                    activityScore = list?.Count(a => a.CompletedAt >= weekAgo) ?? 0,
                    quizCount = list?.Count ?? 0,
                };
            }).OrderBy(m => m.lastName).ThenBy(m => m.firstName).ToList();

            return Ok(members);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting group members");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/institution/groups/{id}/members  ajouter un membre par email.</summary>
    [HttpPost("groups/{id:int}/members")]
    public async Task<IActionResult> AddMember([FromRoute] int id, [FromBody] AddGroupMemberRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            // Seuls les élèves rattachés à l'établissement du compte peuvent être
            // ajoutés : avant, n'importe quel compte WinPlus l'était par son
            // e-mail, et la réponse révélait si l'adresse existait.
            var email = (req.Email ?? string.Empty).Trim().ToLower();
            var ownerInstitutionId = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.InstitutionId).FirstOrDefaultAsync();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
            var isOwnStudent = user != null && ownerInstitutionId != null && await _db.InstitutionStudents
                .AnyAsync(s => s.InstitutionId == ownerInstitutionId && s.StudentId == user.Id);
            if (user == null || !isOwnStudent)
                return NotFound(new { error = "Aucun élève de votre établissement avec cette adresse. Importez-le d'abord dans l'annuaire." });

            var already = await _db.StudyGroupMembers.AnyAsync(m => m.StudyGroupId == id && m.UserId == user.Id);
            if (already) return Conflict(new { error = "Déjà membre du groupe" });

            _db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = id,
                UserId = user.Id,
                Role = "member",
            });
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding group member");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>DELETE /api/institution/groups/{id}/members/{memberId}  retirer un membre.</summary>
    [HttpDelete("groups/{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> RemoveMember([FromRoute] int id, [FromRoute] int memberId)
    {
        try
        {
            var userId = User.GetUserId();
            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == userId);
            if (group == null) return NotFound(new { error = "Groupe introuvable" });

            var member = await _db.StudyGroupMembers.FirstOrDefaultAsync(m => m.StudyGroupId == id && m.UserId == memberId);
            if (member == null) return NotFound(new { error = "Membre introuvable" });

            _db.StudyGroupMembers.Remove(member);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing group member");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Analytics ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/institution/analytics : indicateurs de l'accueil de l'établissement sur 30 jours.
    /// Recalculé le 27/11/2026 : élèves actifs = au moins une activité (quiz, leçon, révision ou téléchargement),
    /// et non plus les seuls téléchargements ; averageScore omis sans quiz (« 0/20 » trompeur avant) ;
    /// activityByDay = élèves actifs chaque jour des 7 derniers jours (avant : téléchargements ÷ 7 sur chaque jour).
    /// Mêmes élèves pour un compte rattaché à un établissement ou un compte qui gère ses propres classes.
    /// La vue complète est GET /api/institution/analytics/overview.
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        try
        {
            var (_, _, _, studentIds) = await ResolveStudentsAsync();
            var now = DateTime.UtcNow;
            var monthStart = now.AddDays(-30);
            var weekStart = now.Date.AddDays(-6);

            var quizzes = await _db.QuizAttempts.AsNoTracking()
                .Where(a => studentIds.Contains(a.UserId) && a.CompletedAt >= monthStart)
                .Select(a => new { a.UserId, a.Score, At = a.CompletedAt }).ToListAsync();
            var downloads = await _db.DownloadHistories.AsNoTracking()
                .Where(d => studentIds.Contains(d.UserId) && d.CreatedAt >= monthStart)
                .Select(d => new { d.UserId, At = d.CreatedAt }).ToListAsync();
            var lessons = await _db.LessonProgress.AsNoTracking()
                .Where(x => studentIds.Contains(x.UserId) && x.UpdatedAt >= monthStart)
                .Select(x => new { x.UserId, At = x.UpdatedAt }).ToListAsync();
            var study = await _db.StudySessions.AsNoTracking()
                .Where(x => studentIds.Contains(x.UserId) && x.CreatedAt >= monthStart)
                .Select(x => new { x.UserId, At = x.CreatedAt }).ToListAsync();

            var events = quizzes.Select(q => (q.UserId, q.At))
                .Concat(downloads.Select(d => (d.UserId, d.At)))
                .Concat(lessons.Select(l => (l.UserId, l.At)))
                .Concat(study.Select(x => (x.UserId, x.At)))
                .ToList();

            string[] dayNames = ["Dim", "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam"];
            var activityByDay = new Dictionary<string, int>
            {
                ["Lun"] = 0, ["Mar"] = 0, ["Mer"] = 0, ["Jeu"] = 0, ["Ven"] = 0, ["Sam"] = 0, ["Dim"] = 0,
            };
            for (var i = 6; i >= 0; i--)
            {
                var day = now.Date.AddDays(-i);
                activityByDay[dayNames[(int)day.DayOfWeek]] = events
                    .Where(e => e.At >= day && e.At < day.AddDays(1)).Select(e => e.UserId).Distinct().Count();
            }

            return Ok(new
            {
                totalStudents = studentIds.Count,
                activeStudents = events.Select(e => e.UserId).Distinct().Count(),
                // Pourcentage de 0 à 100 ; omis (champ absent) quand aucun quiz n'a été fait sur 30 jours.
                averageScore = quizzes.Count > 0 ? Math.Round((double)quizzes.Average(q => q.Score), 1) : (double?)null,
                downloadsThisMonth = downloads.Count,
                quizzesThisMonth = quizzes.Count,
                activityByDay,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution analytics");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Élèves à risque ───────────────────────────────────────────────────────

    /// <summary>
    /// Élèves suivis par l'utilisateur institution : ceux rattachés à son
    /// InstitutionId, ou à défaut les membres de ses groupes. Partagé par
    /// at-risk et action-plan.
    /// </summary>
    private async Task<(int UserId, int? InstitutionId, string? Name, List<int> StudentIds)> ResolveStudentsAsync()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var institutionId = user?.InstitutionId;

        List<int> studentIds;
        if (institutionId.HasValue)
        {
            studentIds = await _db.InstitutionStudents
                .Where(s => s.InstitutionId == institutionId.Value && s.IsActive)
                .Select(s => s.StudentId)
                .ToListAsync();
        }
        else
        {
            var groupIds = await _db.StudyGroups
                .Where(g => g.OwnerId == userId && g.IsActive)
                .Select(g => g.Id)
                .ToListAsync();
            studentIds = await _db.StudyGroupMembers
                .Where(m => groupIds.Contains(m.StudyGroupId) && m.UserId != userId)
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync();
        }

        var name = string.Join(" ", new[] { user?.FirstName, user?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return (userId, institutionId, string.IsNullOrWhiteSpace(name) ? null : name, studentIds);
    }

    /// <summary>GET /api/institution/at-risk  élèves à risque de décrochage.</summary>
    [HttpGet("at-risk")]
    public async Task<IActionResult> GetAtRisk()
    {
        try
        {
            var (_, _, _, studentIds) = await ResolveStudentsAsync();

            if (!studentIds.Any()) return Ok(new List<object>());

            // Calcul réel (27/11/2026), partagé avec la vue d'ensemble des analyses (Services/InstitutionAnalytics).
            var me = User.GetUserId();
            var rows = await InstitutionAnalytics.AtRiskAsync(_db, me, studentIds, 50);
            var result = rows.Select(r => new
            {
                id = r.Id,
                firstName = r.FirstName,
                lastName = r.LastName,
                groupName = r.GroupName,
                level = r.Level,
                riskScore = r.RiskScore,
                riskReason = r.RiskReason,
                weakSubject = r.WeakSubject,
                weakScore = r.WeakScore,
                // Absent (champ omis) quand l'élève n'a fait aucun quiz sur 30 jours : pas un 0 trompeur.
                globalScore = r.GlobalScore,
                hasScores = r.GlobalScore != null,
                inactiveDays = r.InactiveDays,
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting at-risk students");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    // ── Plan d'action ─────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/institution/action-plan  plan d'action IA pour l'institution.
    ///
    /// Module 23 : l'appel Python partait SANS CORPS alors que
    /// POST /api/institution/action-plan exige { institution_id, student_ids,
    /// institution_name } : 422 systématique, masqué par un 200
    /// « Plan d'action non disponible ». La réponse Python (actions[],
    /// week_label, context) est désormais relayée telle quelle, et un échec
    /// renvoie un vrai code d'erreur que l'écran mobile affiche comme tel.
    ///
    /// Sans élève suivi, on n'appelle pas Python : avec une liste vide, il
    /// retomberait sur « tous les élèves de la plateforme » (repli de test
    /// de _resolve_student_ids), et l'institution recevrait un plan calculé
    /// sur les élèves d'autres établissements.
    /// </summary>
    [HttpGet("action-plan")]
    public async Task<IActionResult> GetActionPlan(CancellationToken ct)
    {
        try
        {
            var (userId, institutionId, name, studentIds) = await ResolveStudentsAsync();
            if (studentIds.Count == 0)
            {
                return Ok(new
                {
                    actions = Array.Empty<object>(),
                    week_label = (string?)null,
                    context = new { student_count = 0 },
                    message = "Aucun élève n'est encore rattaché à votre établissement : WinAI ne peut pas proposer de plan.",
                });
            }

            // Lot 9.7b, étape 4 : plan d'action WinAI réservé aux établissements
            // sous licence, prélevé sur le budget WinAI de la licence.
            var licenses = HttpContext.RequestServices.GetRequiredService<Backend.Services.Entitlements.InstitutionLicenseService>();
            if (institutionId is int lid)
            {
                var (budget, used) = await licenses.AiBudgetAsync(lid, ct);
                if (budget <= 0)
                    return StatusCode(403, new { code = "license_required", message = "Les analyses WinAI de l'établissement sont incluses dans la licence WinPlus." });
                if (used >= budget)
                    return StatusCode(402, new { code = "institution_ai_budget_exhausted", message = "Le budget WinAI de votre licence est épuisé pour cette période." });
            }

            var client = _httpClientFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/institution/action-plan")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                {
                    institution_id = institutionId ?? userId,
                    student_ids = studentIds,
                    institution_name = name,
                }),
            };
            var auth = HttpContext.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(auth))
                req.Headers.TryAddWithoutValidation("Authorization", auth);

            var res = await client.SendAsync(req, ct);
            if (institutionId != null)
                await licenses.RecordAiUsageAsync(userId, "action_plan", Backend.Services.Entitlements.IAiFeatureGate.TokensOf(res), ct);
            var content = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Plan d'action WinAI en échec pour l'institution {UserId} : HTTP {Status} {Body}", userId, (int)res.StatusCode, content);
                return StatusCode(res.StatusCode == System.Net.HttpStatusCode.Forbidden ? 403 : 502,
                    new { message = "Plan d'action WinAI indisponible pour le moment." });
            }

            return Content(content, "application/json");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Plan d'action WinAI : appel impossible");
            return StatusCode(502, new { message = "Plan d'action WinAI indisponible pour le moment." });
        }
    }
}
