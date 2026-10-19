using Backend.Services;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Espace institution, phase 2 (étape 2) : annonces aux élèves et aux parents.
///
/// GET    /api/institution/announcements        annonces envoyées par l'établissement
/// POST   /api/institution/announcements        publier (et prévenir chaque destinataire)
/// DELETE /api/institution/announcements/{id}   retirer une annonce
/// GET    /api/institution/announcements/feed   annonces reçues (élève ou parent)
///
/// Destinataires : élèves rattachés ayant accepté le rattachement (toute
/// l'école ou membres d'une classe), et/ou parents liés à ces élèves.
/// </summary>
[ApiController]
[Route("api/institution/announcements")]
[Authorize]
public class InstitutionAnnouncementsController : ControllerBase
{
    private const int MaxTitle = 200;
    private const int MaxContent = 4000;
    private static readonly string[] Types = { "info", "important", "urgent" };

    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionAnnouncementsController> _logger;
    private readonly IServiceScopeFactory _scopes;

    public InstitutionAnnouncementsController(ApplicationDbContext db, ILogger<InstitutionAnnouncementsController> logger, IServiceScopeFactory scopes)
    {
        _db = db;
        _logger = logger;
        _scopes = scopes;
    }

    public record CreateAnnouncementRequest(string Title, string Content, string Audience, string? Type, int? StudyGroupId, DateTime? ExpiresAt);

    private async Task<int?> MyInstitutionIdAsync(int userId) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.InstitutionId).FirstOrDefaultAsync();

    /// <summary>Élèves visés : rattachés et ayant accepté, éventuellement limités à une classe.</summary>
    private async Task<List<int>> TargetStudentIdsAsync(int institutionId, int? groupId)
    {
        var students = _db.InstitutionStudents.AsNoTracking()
            .Where(s => s.InstitutionId == institutionId && s.IsActive
                        && s.ConfirmationStatus == InstitutionAttachmentStatuses.Accepted)
            .Select(s => s.StudentId);

        if (groupId != null)
        {
            var members = _db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudyGroupId == groupId && m.Role != InstitutionTeachersController.TeacherRole)
                .Select(m => m.UserId);
            students = students.Where(id => members.Contains(id));
        }
        return await students.Distinct().ToListAsync();
    }

    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null) return Ok(new { data = Array.Empty<object>(), success = true });

            var list = await _db.InstitutionAnnouncements.AsNoTracking()
                .Where(a => a.InstitutionId == institutionId && !a.IsDeleted)
                .OrderByDescending(a => a.CreatedAt)
                .Take(100)
                .Select(a => new
                {
                    a.Id, a.Title, a.Content, a.Audience, a.Type, a.RecipientCount, a.ExpiresAt, a.CreatedAt,
                    groupId = a.StudyGroupId,
                    groupName = a.StudyGroup != null ? a.StudyGroup.Name : null,
                })
                .ToListAsync();
            return Ok(new { data = list, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing institution announcements");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest req)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            if (institutionId == null) return BadRequest(new { error = "Compte non rattaché à un établissement." });

            var title = (req.Title ?? string.Empty).Trim();
            var content = (req.Content ?? string.Empty).Trim();
            if (title.Length == 0 || content.Length == 0) return BadRequest(new { error = "Titre et message sont obligatoires." });
            if (title.Length > MaxTitle) return BadRequest(new { error = $"Titre limité à {MaxTitle} caractères." });
            if (content.Length > MaxContent) return BadRequest(new { error = $"Message limité à {MaxContent} caractères." });
            if (!InstitutionAnnouncementAudiences.IsValid(req.Audience)) return BadRequest(new { error = "Destinataires invalides." });
            var type = Types.Contains(req.Type) ? req.Type! : "info";
            if (req.ExpiresAt != null && req.ExpiresAt <= DateTime.UtcNow) return BadRequest(new { error = "La date de fin doit être dans le futur." });

            if (req.StudyGroupId != null)
            {
                var ownGroup = await _db.StudyGroups.AnyAsync(g => g.Id == req.StudyGroupId && g.OwnerId == me && g.IsActive);
                if (!ownGroup) return NotFound(new { error = "Classe introuvable." });
            }

            var studentIds = await TargetStudentIdsAsync(institutionId.Value, req.StudyGroupId);
            var recipients = new HashSet<int>();
            if (InstitutionAnnouncementAudiences.ForStudents(req.Audience)) recipients.UnionWith(studentIds);
            if (InstitutionAnnouncementAudiences.ForParents(req.Audience))
            {
                var parents = await _db.ParentStudentLinks.AsNoTracking()
                    .Where(l => l.Status == "accepted" && studentIds.Contains(l.StudentId))
                    .Select(l => l.ParentId)
                    .Distinct()
                    .ToListAsync();
                recipients.UnionWith(parents);
            }

            var announcement = new InstitutionAnnouncement
            {
                InstitutionId = institutionId.Value,
                StudyGroupId = req.StudyGroupId,
                AuthorUserId = me,
                Audience = req.Audience,
                Type = type,
                Title = title,
                Content = content,
                RecipientCount = recipients.Count,
                ExpiresAt = req.ExpiresAt,
            };
            _db.InstitutionAnnouncements.Add(announcement);
            await _db.SaveChangesAsync();

            var institutionName = await _db.Institutions.Where(i => i.Id == institutionId).Select(i => i.Name).FirstOrDefaultAsync() ?? "Votre établissement";
            Notify(institutionName, announcement.Id, title, type, recipients.ToList());

            return Ok(new { data = new { announcement.Id, recipientCount = recipients.Count }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating institution announcement");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Prévient chaque destinataire (liste in-app et ntfy), hors requête.</summary>
    private void Notify(string institutionName, int announcementId, string title, string type, IReadOnlyList<int> userIds)
    {
        if (userIds.Count == 0) return;
        var scopes = _scopes;
        var logger = _logger;
        var priority = type == "urgent" ? "high" : "default";
        _ = Task.Run(async () =>
        {
            using var scope = scopes.CreateScope();
            var ntfy = scope.ServiceProvider.GetRequiredService<Backend.Services.INtfyService>();
            var db = scope.ServiceProvider.GetService<ApplicationDbContext>();
            foreach (var id in userIds)
            {
                try
                {
                    // Lot 10.6 : libellé dans la langue du destinataire ; le titre
                    // rédigé par l'établissement est repris tel quel.
                    await ntfy.PublishLocalizedAsync(db, $"winplus-user-{id}",
                        new($"{institutionName} : {title}", $"{institutionName}: {title}"),
                        new("Nouvelle annonce de votre établissement. Ouvrez WinPlus pour la lire.",
                            "New announcement from your school. Open WinPlus to read it."),
                        priority: priority, userId: id, type: "InstitutionAnnouncement",
                        relatedEntityType: "InstitutionAnnouncement", relatedEntityId: announcementId);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Annonce {AnnouncementId} non notifiée à {UserId}", announcementId, id); }
            }
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            var me = User.GetUserId();
            if (User.GetUserRole() != "institution") return Forbid();
            var institutionId = await MyInstitutionIdAsync(me);
            var a = await _db.InstitutionAnnouncements.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == institutionId && !x.IsDeleted);
            if (a == null) return NotFound(new { error = "Annonce introuvable." });
            a.IsDeleted = true;
            a.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting institution announcement");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Annonces reçues : un élève voit celles de ses établissements (audience
    /// élèves ou tous) ; un parent celles des établissements de ses enfants
    /// (audience parents ou tous). Une annonce de classe ne concerne que ses membres.
    /// </summary>
    [HttpGet("feed")]
    public async Task<IActionResult> Feed()
    {
        try
        {
            var me = User.GetUserId();
            var role = User.GetUserRole();
            var now = DateTime.UtcNow;

            List<int> studentIds;
            bool asParent;
            if (role == "student") { studentIds = new List<int> { me }; asParent = false; }
            else if (role == "parent")
            {
                studentIds = await _db.ParentStudentLinks.AsNoTracking()
                    .Where(l => l.ParentId == me && l.Status == "accepted")
                    .Select(l => l.StudentId).ToListAsync();
                asParent = true;
            }
            else return Ok(new { data = Array.Empty<object>(), success = true });

            if (studentIds.Count == 0) return Ok(new { data = Array.Empty<object>(), success = true });

            var attachments = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => studentIds.Contains(s.StudentId) && s.IsActive
                            && s.ConfirmationStatus == InstitutionAttachmentStatuses.Accepted)
                .Select(s => s.InstitutionId)
                .Distinct().ToListAsync();
            if (attachments.Count == 0) return Ok(new { data = Array.Empty<object>(), success = true });

            var groupIds = await _db.StudyGroupMembers.AsNoTracking()
                .Where(m => studentIds.Contains(m.UserId) && m.Role != InstitutionTeachersController.TeacherRole)
                .Select(m => m.StudyGroupId).Distinct().ToListAsync();

            var audiences = asParent
                ? new[] { InstitutionAnnouncementAudiences.Parents, InstitutionAnnouncementAudiences.All }
                : new[] { InstitutionAnnouncementAudiences.Students, InstitutionAnnouncementAudiences.All };

            var list = await _db.InstitutionAnnouncements.AsNoTracking()
                .Where(a => !a.IsDeleted && attachments.Contains(a.InstitutionId)
                            && audiences.Contains(a.Audience)
                            && (a.ExpiresAt == null || a.ExpiresAt > now)
                            && (a.StudyGroupId == null || groupIds.Contains(a.StudyGroupId.Value)))
                .OrderByDescending(a => a.CreatedAt)
                .Take(20)
                .Select(a => new
                {
                    a.Id, a.Title, a.Content, a.Type, a.CreatedAt, a.ExpiresAt,
                    institutionName = a.Institution != null ? a.Institution.Name : null,
                    groupName = a.StudyGroup != null ? a.StudyGroup.Name : null,
                })
                .ToListAsync();
            return Ok(new { data = list, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting institution announcements feed");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
