using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public record InstitutionLinkRequest(int? InstitutionId, int? TeacherId);
public record AssignStudentRequest(int TeacherId, int StudentId);
public record RequestStudentAccessRequest(int StudentId, int InstitutionId);

/// <summary>
/// Fusion Réseau/Mode Tuteur (design confirmé avec le produit) :
///   1. Affiliation prof/tuteur ↔ institution, bidirectionnelle, révocable des
///      deux côtés — même mécanique que TeacherStudentLinksController.
///   2. Une fois affilié, le prof voit (lecture seule) les élèves de
///      l'institution (InstitutionStudents), sans pouvoir les contacter.
///   3. Passage visible → en contact par l'une des 3 portes :
///      a) l'institution assigne directement (AssignStudent)
///      b) le prof demande l'accès, approuvable par l'élève, l'institution,
///         OU le parent lié — le premier qui répond suffit (RequestStudentAccess
///         + Accept/Reject sur TeacherStudentAccessRequests)
///      c) l'élève écrit en premier (déjà couvert par la messagerie existante,
///         rien à faire ici)
/// </summary>
[ApiController]
[Route("api/institution-network")]
[Authorize]
public class InstitutionNetworkController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InstitutionNetworkController> _logger;

    public InstitutionNetworkController(ApplicationDbContext db, ILogger<InstitutionNetworkController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private async Task<int?> MyInstitutionIdAsync(int userId) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.InstitutionId).FirstOrDefaultAsync();

    // ───────────────────────── Recherche ─────────────────────────

    /// <summary>
    /// Institutions disponibles pour affiliation. Sans texte de recherche,
    /// renvoie la liste (l'utilisateur doit pouvoir parcourir les institutions
    /// disponibles pour envoyer une demande, pas seulement taper un nom exact
    /// qu'il ne connaît pas encore).
    /// </summary>
    [HttpGet("search-institutions")]
    public async Task<IActionResult> SearchInstitutions([FromQuery] string? q)
    {
        var query = _db.Institutions.AsNoTracking().Where(i => !i.IsDeleted && i.IsActive);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var lq = q.ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(lq));
        }

        var results = await query
            .OrderBy(i => i.Name)
            .Select(i => new { i.Id, i.Name, i.City, i.Country, i.Type })
            .Take(30)
            .ToListAsync();

        return Ok(results);
    }

    /// <summary>Une institution cherche un prof à inviter.</summary>
    [HttpGet("search-teachers")]
    public async Task<IActionResult> SearchTeachers([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { error = "Requête trop courte" });

        var lq = q.ToLower();
        var results = await _db.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && u.Role == "teacher" &&
                        (u.Email.ToLower().Contains(lq) || (u.FirstName + " " + u.LastName).ToLower().Contains(lq)))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.AvatarUrl })
            .Take(10)
            .ToListAsync();

        return Ok(results);
    }

    // ───────────────────────── Affiliation ─────────────────────────

    /// <summary>Envoie une demande d'affiliation, dans un sens ou l'autre.</summary>
    [HttpPost("affiliation/request")]
    public async Task<IActionResult> RequestAffiliation([FromBody] InstitutionLinkRequest req)
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();

        int institutionId, teacherId;
        if (myRole == "teacher")
        {
            if (req.InstitutionId == null) return BadRequest(new { error = "InstitutionId requis." });
            institutionId = req.InstitutionId.Value;
            teacherId = me;
        }
        else if (myRole == "institution")
        {
            if (req.TeacherId == null) return BadRequest(new { error = "TeacherId requis." });
            var myInstitutionId = await MyInstitutionIdAsync(me);
            if (myInstitutionId == null) return BadRequest(new { error = "Compte institution non rattaché à un établissement." });
            institutionId = myInstitutionId.Value;
            teacherId = req.TeacherId.Value;
        }
        else
        {
            return Forbid();
        }

        var target = await _db.Users.FindAsync(teacherId);
        if (target == null || target.Role != "teacher")
            return NotFound(new { error = "Professeur introuvable." });
        var institution = await _db.Institutions.FindAsync(institutionId);
        if (institution == null || institution.IsDeleted)
            return NotFound(new { error = "Institution introuvable." });

        var existing = await _db.InstitutionTeacherLinks
            .FirstOrDefaultAsync(l => l.InstitutionId == institutionId && l.TeacherId == teacherId);

        if (existing != null)
        {
            if (existing.Status == "accepted") return Conflict(new { error = "Affiliation déjà active." });
            if (existing.Status == "pending") return Conflict(new { error = "Demande déjà envoyée." });
            existing.Status = "pending";
            existing.InitiatedBy = me;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.InstitutionTeacherLinks.Add(new InstitutionTeacherLink
            {
                InstitutionId = institutionId,
                TeacherId = teacherId,
                Status = "pending",
                InitiatedBy = me,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Demandes d'affiliation en attente, reçues par moi.</summary>
    [HttpGet("affiliation/pending")]
    public async Task<IActionResult> GetPendingAffiliations()
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();

        IQueryable<InstitutionTeacherLink> query = _db.InstitutionTeacherLinks
            .AsNoTracking()
            .Where(l => l.Status == "pending" && l.InitiatedBy != me);

        if (myRole == "teacher")
            query = query.Where(l => l.TeacherId == me);
        else if (myRole == "institution")
        {
            var myInstitutionId = await MyInstitutionIdAsync(me);
            query = query.Where(l => l.InstitutionId == myInstitutionId);
        }
        else return Forbid();

        var pending = await query
            .Include(l => l.Institution)
            .Include(l => l.Teacher)
            .Select(l => new
            {
                l.Id,
                l.Status,
                l.CreatedAt,
                Institution = l.Institution == null ? null : new { l.Institution.Id, l.Institution.Name, l.Institution.City },
                Teacher = l.Teacher == null ? null : new { l.Teacher.Id, l.Teacher.FirstName, l.Teacher.LastName, l.Teacher.AvatarUrl },
            })
            .ToListAsync();

        return Ok(pending);
    }

    /// <summary>Accepte une demande d'affiliation.</summary>
    [HttpPut("affiliation/{id:int}/accept")]
    public async Task<IActionResult> AcceptAffiliation(int id)
    {
        var me = User.GetUserId();
        var link = await _db.InstitutionTeacherLinks.FindAsync(id);
        if (link == null) return NotFound();
        if (link.InitiatedBy == me) return BadRequest(new { error = "Vous ne pouvez pas accepter votre propre demande." });
        if (!await CanActOnAffiliationAsync(link, me)) return Forbid();

        link.Status = "accepted";
        link.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Rejette une demande d'affiliation.</summary>
    [HttpPut("affiliation/{id:int}/reject")]
    public async Task<IActionResult> RejectAffiliation(int id)
    {
        var me = User.GetUserId();
        var link = await _db.InstitutionTeacherLinks.FindAsync(id);
        if (link == null) return NotFound();
        if (!await CanActOnAffiliationAsync(link, me)) return Forbid();

        link.Status = "rejected";
        link.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Affiliations actives (prof : ses institutions ; institution : ses profs).</summary>
    [HttpGet("affiliation/mine")]
    public async Task<IActionResult> GetMyAffiliations()
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();

        if (myRole == "teacher")
        {
            var links = await _db.InstitutionTeacherLinks.AsNoTracking()
                .Where(l => l.TeacherId == me && l.Status == "accepted")
                .Include(l => l.Institution)
                .Select(l => new { l.Id, l.CreatedAt, Institution = new { l.Institution!.Id, l.Institution.Name, l.Institution.City, l.Institution.Type } })
                .ToListAsync();
            return Ok(links);
        }
        if (myRole == "institution")
        {
            var myInstitutionId = await MyInstitutionIdAsync(me);
            var links = await _db.InstitutionTeacherLinks.AsNoTracking()
                .Where(l => l.InstitutionId == myInstitutionId && l.Status == "accepted")
                .Include(l => l.Teacher)
                .Select(l => new { l.Id, l.CreatedAt, Teacher = new { l.Teacher!.Id, l.Teacher.FirstName, l.Teacher.LastName, l.Teacher.AvatarUrl } })
                .ToListAsync();
            return Ok(links);
        }
        return Forbid();
    }

    /// <summary>Révoque une affiliation — l'institution ou le prof peut le faire.</summary>
    [HttpDelete("affiliation/{id:int}")]
    public async Task<IActionResult> RevokeAffiliation(int id)
    {
        var me = User.GetUserId();
        var link = await _db.InstitutionTeacherLinks.FindAsync(id);
        if (link == null) return NotFound();
        if (!await CanActOnAffiliationAsync(link, me)) return Forbid();

        _db.InstitutionTeacherLinks.Remove(link);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    private async Task<bool> CanActOnAffiliationAsync(InstitutionTeacherLink link, int userId)
    {
        if (link.TeacherId == userId) return true;
        var myInstitutionId = await MyInstitutionIdAsync(userId);
        return myInstitutionId == link.InstitutionId;
    }

    // ───────────────────── Visibilité (lecture seule) ─────────────────────

    /// <summary>
    /// Élèves des institutions où le prof est affilié — lecture seule, pas de
    /// contact tant qu'aucune des 3 portes n'a été franchie (voir en-tête).
    /// </summary>
    [HttpGet("visible-students")]
    public async Task<IActionResult> GetVisibleStudents()
    {
        var me = User.GetUserId();

        var institutionIds = await _db.InstitutionTeacherLinks.AsNoTracking()
            .Where(l => l.TeacherId == me && l.Status == "accepted")
            .Select(l => l.InstitutionId)
            .ToListAsync();

        if (institutionIds.Count == 0) return Ok(Array.Empty<object>());

        var alreadyLinkedIds = await _db.TeacherStudentLinks.AsNoTracking()
            .Where(l => l.TeacherId == me && l.Status == "accepted")
            .Select(l => l.StudentId)
            .ToListAsync();

        var pendingRequestStudentIds = await _db.TeacherStudentAccessRequests.AsNoTracking()
            .Where(r => r.TeacherId == me && r.Status == "pending")
            .Select(r => r.StudentId)
            .ToListAsync();

        var students = await _db.InstitutionStudents.AsNoTracking()
            .Where(s => institutionIds.Contains(s.InstitutionId) && s.Student != null && !s.Student.IsDeleted)
            .Select(s => new
            {
                s.StudentId,
                s.InstitutionId,
                InstitutionName = s.Institution!.Name,
                FirstName = s.Student!.FirstName,
                LastName = s.Student.LastName,
                AvatarUrl = s.Student.AvatarUrl,
                Level = s.Level ?? s.Student.Level,
                GroupName = s.GroupName,
            })
            .ToListAsync();

        var result = students.Select(s => new
        {
            s.StudentId,
            s.InstitutionId,
            s.InstitutionName,
            s.FirstName,
            s.LastName,
            s.AvatarUrl,
            s.Level,
            s.GroupName,
            hasContact = alreadyLinkedIds.Contains(s.StudentId),
            requestPending = pendingRequestStudentIds.Contains(s.StudentId),
        });

        return Ok(result);
    }

    // ───────────────────── Portes vers le contact ─────────────────────

    /// <summary>Porte 1 — l'institution assigne directement un élève à un prof affilié.</summary>
    [HttpPost("assign-student")]
    public async Task<IActionResult> AssignStudent([FromBody] AssignStudentRequest req)
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();
        if (myRole != "institution") return Forbid();

        var myInstitutionId = await MyInstitutionIdAsync(me);
        if (myInstitutionId == null) return BadRequest(new { error = "Compte institution non rattaché." });

        var affiliated = await _db.InstitutionTeacherLinks.AnyAsync(l =>
            l.InstitutionId == myInstitutionId && l.TeacherId == req.TeacherId && l.Status == "accepted");
        if (!affiliated) return BadRequest(new { error = "Ce professeur n'est pas affilié à votre établissement." });

        var studentBelongs = await _db.InstitutionStudents.AnyAsync(s =>
            s.InstitutionId == myInstitutionId && s.StudentId == req.StudentId);
        if (!studentBelongs) return BadRequest(new { error = "Cet élève n'appartient pas à votre établissement." });

        var existing = await _db.TeacherStudentLinks.FirstOrDefaultAsync(l =>
            l.TeacherId == req.TeacherId && l.StudentId == req.StudentId);

        if (existing != null)
        {
            existing.Status = "accepted";
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.TeacherStudentLinks.Add(new TeacherStudentLink
            {
                TeacherId = req.TeacherId,
                StudentId = req.StudentId,
                Status = "accepted",
                InitiatedBy = me,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Porte 2 — le prof demande l'accès à un élève visible.</summary>
    [HttpPost("access-requests")]
    public async Task<IActionResult> RequestStudentAccess([FromBody] RequestStudentAccessRequest req)
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();
        if (myRole != "teacher") return Forbid();

        var affiliated = await _db.InstitutionTeacherLinks.AnyAsync(l =>
            l.TeacherId == me && l.InstitutionId == req.InstitutionId && l.Status == "accepted");
        if (!affiliated) return BadRequest(new { error = "Vous n'êtes pas affilié à cette institution." });

        var studentBelongs = await _db.InstitutionStudents.AnyAsync(s =>
            s.InstitutionId == req.InstitutionId && s.StudentId == req.StudentId);
        if (!studentBelongs) return BadRequest(new { error = "Cet élève n'appartient pas à cette institution." });

        var alreadyLinked = await _db.TeacherStudentLinks.AnyAsync(l =>
            l.TeacherId == me && l.StudentId == req.StudentId && l.Status == "accepted");
        if (alreadyLinked) return Conflict(new { error = "Déjà en contact avec cet élève." });

        var existing = await _db.TeacherStudentAccessRequests.FirstOrDefaultAsync(r =>
            r.TeacherId == me && r.StudentId == req.StudentId && r.Status == "pending");
        if (existing != null) return Conflict(new { error = "Demande déjà envoyée." });

        _db.TeacherStudentAccessRequests.Add(new TeacherStudentAccessRequest
        {
            TeacherId = me,
            StudentId = req.StudentId,
            InstitutionId = req.InstitutionId,
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>
    /// Demandes d'accès en attente que JE peux approuver — l'élève lui-même,
    /// l'institution concernée, ou un parent lié à cet élève.
    /// </summary>
    [HttpGet("access-requests/pending")]
    public async Task<IActionResult> GetPendingAccessRequests()
    {
        var me = User.GetUserId();
        var myRole = User.GetUserRole();

        IQueryable<TeacherStudentAccessRequest> query = _db.TeacherStudentAccessRequests.AsNoTracking()
            .Where(r => r.Status == "pending");

        if (myRole == "student")
        {
            query = query.Where(r => r.StudentId == me);
        }
        else if (myRole == "institution")
        {
            var myInstitutionId = await MyInstitutionIdAsync(me);
            query = query.Where(r => r.InstitutionId == myInstitutionId);
        }
        else if (myRole == "parent")
        {
            var myChildrenIds = await _db.ParentStudentLinks.AsNoTracking()
                .Where(l => l.ParentId == me && l.Status == "accepted")
                .Select(l => l.StudentId)
                .ToListAsync();
            query = query.Where(r => myChildrenIds.Contains(r.StudentId));
        }
        else
        {
            return Ok(Array.Empty<object>());
        }

        var pending = await query
            .Include(r => r.Teacher)
            .Include(r => r.Student)
            .Include(r => r.Institution)
            .Select(r => new
            {
                r.Id,
                r.CreatedAt,
                Teacher = new { r.Teacher!.Id, r.Teacher.FirstName, r.Teacher.LastName, r.Teacher.AvatarUrl },
                Student = new { r.Student!.Id, r.Student.FirstName, r.Student.LastName },
                Institution = new { r.Institution!.Id, r.Institution.Name },
            })
            .ToListAsync();

        return Ok(pending);
    }

    /// <summary>Approuve une demande d'accès — crée le lien prof-élève en accepted.</summary>
    [HttpPut("access-requests/{id:int}/accept")]
    public async Task<IActionResult> AcceptAccessRequest(int id)
    {
        var me = User.GetUserId();
        var request = await _db.TeacherStudentAccessRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != "pending") return Conflict(new { error = "Cette demande a déjà été traitée." });
        if (!await CanRespondToAccessRequestAsync(request, me)) return Forbid();

        request.Status = "accepted";
        request.RespondedBy = me;
        request.RespondedAt = DateTime.UtcNow;

        var existingLink = await _db.TeacherStudentLinks.FirstOrDefaultAsync(l =>
            l.TeacherId == request.TeacherId && l.StudentId == request.StudentId);
        if (existingLink != null)
        {
            existingLink.Status = "accepted";
            existingLink.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.TeacherStudentLinks.Add(new TeacherStudentLink
            {
                TeacherId = request.TeacherId,
                StudentId = request.StudentId,
                Status = "accepted",
                InitiatedBy = request.TeacherId,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>Rejette une demande d'accès.</summary>
    [HttpPut("access-requests/{id:int}/reject")]
    public async Task<IActionResult> RejectAccessRequest(int id)
    {
        var me = User.GetUserId();
        var request = await _db.TeacherStudentAccessRequests.FindAsync(id);
        if (request == null) return NotFound();
        if (request.Status != "pending") return Conflict(new { error = "Cette demande a déjà été traitée." });
        if (!await CanRespondToAccessRequestAsync(request, me)) return Forbid();

        request.Status = "rejected";
        request.RespondedBy = me;
        request.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    private async Task<bool> CanRespondToAccessRequestAsync(TeacherStudentAccessRequest request, int userId)
    {
        if (request.StudentId == userId) return true;

        var myInstitutionId = await MyInstitutionIdAsync(userId);
        if (myInstitutionId != null && myInstitutionId == request.InstitutionId) return true;

        var isLinkedParent = await _db.ParentStudentLinks.AnyAsync(l =>
            l.ParentId == userId && l.StudentId == request.StudentId && l.Status == "accepted");
        return isLinkedParent;
    }
}
