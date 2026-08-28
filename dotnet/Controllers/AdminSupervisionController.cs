using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers;

/// <summary>
/// Supervision transversale de l'administration.
/// Complète le dashboard admin sur ce qu'il ne couvrait pas : statistiques
/// consolidées en un appel, groupes d'étude, classes d'enseignants, crédits
/// parents, licences par institution, et export CSV de chacun de ces registres.
///
/// Tout est lu en base. Les compteurs nuls sont renvoyés tels quels : le
/// frontend affiche un tiret plutôt qu'un zéro trompeur.
///
/// GET /api/admin/supervision/stats
/// GET /api/admin/supervision/study-groups
/// GET /api/admin/supervision/teacher-classes
/// GET /api/admin/supervision/parent-credits
/// GET /api/admin/supervision/institution-licences
/// GET /api/admin/supervision/export/{registry}
/// </summary>
[ApiController]
[Route("api/admin/supervision")]
[Authorize(Policy = "AdminOnly")]
public class AdminSupervisionController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AdminSupervisionController> _logger;

    public AdminSupervisionController(ApplicationDbContext db, ILogger<AdminSupervisionController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Tableau de bord consolidé : un seul aller-retour pour la vue globale.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int period = 30)
    {
        try
        {
            var days = period is 7 or 30 or 90 or 365 ? period : 30;
            var since = DateTime.UtcNow.Date.AddDays(-days);
            var since24h = DateTime.UtcNow.AddDays(-1);

            var usersByRole = await _db.Users.AsNoTracking()
                .Where(u => !u.IsDeleted)
                .GroupBy(u => u.Role)
                .Select(g => new { role = g.Key, count = g.Count() })
                .ToListAsync();

            var newUsers = await _db.Users.CountAsync(u => !u.IsDeleted && u.CreatedAt >= since);
            var activeUsers = await _db.Users.CountAsync(u => !u.IsDeleted && u.LastLoginAt >= since24h);
            var unverified = await _db.Users.CountAsync(u => !u.IsDeleted && !u.IsEmailVerified);

            var subjectsPublished = await _db.Subjects.CountAsync(s => !s.IsDeleted && s.IsPublished);
            var subjectsDraft = await _db.Subjects.CountAsync(s => !s.IsDeleted && !s.IsPublished);
            var contentsByStatus = await _db.CourseContents.AsNoTracking()
                .GroupBy(c => c.Status)
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToListAsync();

            var orders = await _db.Orders.AsNoTracking()
                .Where(o => !o.IsDeleted && o.CreatedAt >= since)
                .Select(o => new { o.Status, o.TotalAmount })
                .ToListAsync();

            var revenue = orders
                .Where(o => o.Status == "paid" || o.Status == "Completed" || o.Status == "completed")
                .Sum(o => o.TotalAmount);

            var activeSubscriptions = await _db.Subscriptions
                .CountAsync(s => !s.IsDeleted && s.Status == "active");

            var quizAttempts = await _db.QuizAttempts
                .CountAsync(a => a.IsCompleted && a.CompletedAt >= since);

            var studyMinutes = await _db.StudySessions
                .Where(s => s.CreatedAt >= since)
                .SumAsync(s => (int?)s.Duration) ?? 0;

            var downloads = await _db.DownloadHistories.CountAsync(d => d.CreatedAt >= since);

            var forumThreads = await _db.ForumThreads.CountAsync(t => t.CreatedAt >= since);
            var forumPosts = await _db.ForumPosts.CountAsync(p => p.CreatedAt >= since);

            var studyGroups = await _db.StudyGroups.CountAsync(g => g.IsActive);
            var teacherClasses = await _db.TeacherClasses.CountAsync(c => c.IsActive);
            var institutionLinks = await _db.InstitutionStudents.CountAsync(s => s.IsActive);

            return Ok(new
            {
                data = new
                {
                    periodDays = days,
                    comptes = new
                    {
                        parRole = usersByRole,
                        total = usersByRole.Sum(u => u.count),
                        nouveaux = newUsers,
                        actifs24h = activeUsers,
                        emailNonVerifie = unverified
                    },
                    contenu = new
                    {
                        epreuvesPubliees = subjectsPublished,
                        epreuvesBrouillon = subjectsDraft,
                        publicationsParStatut = contentsByStatus
                    },
                    commerce = new
                    {
                        commandes = orders.Count,
                        chiffreAffaires = revenue,
                        devise = "XAF",
                        abonnementsActifs = activeSubscriptions
                    },
                    apprentissage = new
                    {
                        quizTermines = quizAttempts,
                        heuresEtude = Math.Round(studyMinutes / 60d, 1),
                        telechargements = downloads
                    },
                    communaute = new
                    {
                        filsForum = forumThreads,
                        messagesForum = forumPosts,
                        groupesEtude = studyGroups,
                        classes = teacherClasses,
                        elevesRattaches = institutionLinks
                    }
                },
                success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building admin supervision stats");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("study-groups")]
    public async Task<IActionResult> GetStudyGroups([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 25;

            var query = _db.StudyGroups.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(g => g.Name.ToLower().Contains(term)
                                      || (g.Subject != null && g.Subject.ToLower().Contains(term)));
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(g => g.LastActivityAt ?? g.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new
                {
                    g.Id, g.Name, g.Subject, g.JoinCode, g.IsActive, g.CreatedAt, g.LastActivityAt,
                    ownerId    = g.OwnerId,
                    ownerName  = g.Owner == null ? null : (g.Owner.FirstName + " " + g.Owner.LastName).Trim(),
                    ownerEmail = g.Owner == null ? null : g.Owner.Email,
                    memberCount = _db.StudyGroupMembers.Count(m => m.StudyGroupId == g.Id)
                })
                .ToListAsync();

            return Ok(new { data = items, total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing study groups (admin)");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Archive un groupe d'étude signalé (modération).</summary>
    [HttpPost("study-groups/{id:int}/archive")]
    public async Task<IActionResult> ArchiveStudyGroup([FromRoute] int id)
    {
        try
        {
            var group = await _db.StudyGroups.FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound(new { success = false, error = "Groupe introuvable." });

            group.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(new { data = new { group.Id, group.IsActive }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving study group {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("teacher-classes")]
    public async Task<IActionResult> GetTeacherClasses([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 25;

            var total = await _db.TeacherClasses.CountAsync(c => c.IsActive);
            var items = await _db.TeacherClasses.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id, c.Name, c.Level, c.AcademicYear, c.CreatedAt,
                    teacherId    = c.TeacherId,
                    teacherName  = c.Teacher == null ? null : (c.Teacher.FirstName + " " + c.Teacher.LastName).Trim(),
                    teacherEmail = c.Teacher == null ? null : c.Teacher.Email,
                    studentCount = _db.TeacherClassStudents.Count(cs => cs.TeacherClassId == c.Id)
                })
                .ToListAsync();

            return Ok(new { data = items, total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize), success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing teacher classes (admin)");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("parent-credits")]
    public async Task<IActionResult> GetParentCredits([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 25;

            var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var grouped = _db.ParentCreditLedgers.AsNoTracking()
                .Where(l => l.PeriodStart == periodStart)
                .GroupBy(l => l.ParentId)
                .Select(g => new
                {
                    parentId  = g.Key,
                    alloue    = g.Where(x => x.EntryType == "allocation").Sum(x => x.Amount),
                    consomme  = g.Where(x => x.EntryType == "consumption").Sum(x => x.Amount),
                    rembourse = g.Where(x => x.EntryType == "refund").Sum(x => x.Amount),
                    mouvements = g.Count()
                });

            var total = await grouped.CountAsync();

            var page1 = await grouped
                .OrderByDescending(x => x.consomme)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var ids = page1.Select(x => x.parentId).ToList();
            var parents = await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync();

            var items = page1.Select(x =>
            {
                var parent = parents.FirstOrDefault(p => p.Id == x.parentId);
                return new
                {
                    parentId = x.parentId,
                    parentName = parent == null ? null : ((parent.FirstName + " " + parent.LastName).Trim()),
                    parentEmail = parent?.Email,
                    x.alloue, x.consomme, x.rembourse,
                    restant = x.alloue - x.consomme + x.rembourse,
                    x.mouvements
                };
            }).ToList();

            return Ok(new { data = items, total, page, pageSize, periodStart, devise = "XAF", success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing parent credits (admin)");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpGet("institution-licences")]
    public async Task<IActionResult> GetInstitutionLicences()
    {
        try
        {
            var items = await _db.Institutions.AsNoTracking()
                .Where(i => !i.IsDeleted)
                .Select(i => new
                {
                    i.Id, i.Name, i.City, i.Country, i.Type, i.IsActive,
                    elevesRattaches = _db.InstitutionStudents.Count(s => s.InstitutionId == i.Id),
                    elevesActifs    = _db.InstitutionStudents.Count(s => s.InstitutionId == i.Id && s.IsActive),
                    comptesAdmin    = _db.Users.Count(u => u.InstitutionId == i.Id && !u.IsDeleted)
                })
                .OrderByDescending(x => x.elevesRattaches)
                .ToListAsync();

            return Ok(new { data = items, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing institution licences (admin)");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>
    /// Export CSV d'un registre : users | subjects | orders | study-groups |
    /// teacher-classes | parent-credits | institution-licences.
    /// </summary>
    [HttpGet("export/{registry}")]
    public async Task<IActionResult> Export([FromRoute] string registry)
    {
        try
        {
            string[] header;
            List<string[]> rows;

            switch (registry)
            {
                case "users":
                    header = new[] { "Id", "Email", "Prenom", "Nom", "Role", "Niveau", "EmailVerifie", "Actif", "DerniereConnexion", "Inscription" };
                    rows = (await _db.Users.AsNoTracking().Where(u => !u.IsDeleted)
                        .OrderBy(u => u.Id)
                        .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Role, u.Level, u.IsEmailVerified, u.IsActive, u.LastLoginAt, u.CreatedAt })
                        .ToListAsync())
                        .Select(u => new[] { u.Id.ToString(), u.Email, u.FirstName ?? "", u.LastName ?? "", u.Role, u.Level ?? "",
                                             u.IsEmailVerified ? "oui" : "non", u.IsActive ? "oui" : "non",
                                             u.LastLoginAt?.ToString("u") ?? "", u.CreatedAt.ToString("u") }).ToList();
                    break;

                case "subjects":
                    header = new[] { "Id", "Titre", "Categorie", "Prix", "Publie", "Inscriptions", "Telechargements", "Note", "Avis", "Creation" };
                    rows = (await _db.Subjects.AsNoTracking().Where(s => !s.IsDeleted)
                        .OrderBy(s => s.Id)
                        .Select(s => new { s.Id, s.Title, s.Category, s.Price, s.IsPublished, s.EnrollmentCount, s.DownloadCount, s.AverageRating, s.TotalRatings, s.CreatedAt })
                        .ToListAsync())
                        .Select(s => new[] { s.Id.ToString(), s.Title, s.Category ?? "", s.Price.ToString("0.##"),
                                             s.IsPublished ? "oui" : "non", s.EnrollmentCount.ToString(), (s.DownloadCount ?? 0).ToString(),
                                             s.AverageRating.ToString("0.##"), s.TotalRatings.ToString(), s.CreatedAt.ToString("u") }).ToList();
                    break;

                case "orders":
                    header = new[] { "Id", "Numero", "UtilisateurId", "EmailInvite", "Montant", "Statut", "MoyenPaiement", "Date" };
                    rows = (await _db.Orders.AsNoTracking().Where(o => !o.IsDeleted)
                        .OrderByDescending(o => o.CreatedAt)
                        .Select(o => new { o.Id, o.OrderNumber, o.UserId, o.GuestEmail, o.TotalAmount, o.Status, o.PaymentMethod, o.CreatedAt })
                        .ToListAsync())
                        .Select(o => new[] { o.Id.ToString(), o.OrderNumber, o.UserId?.ToString() ?? "", o.GuestEmail ?? "",
                                             o.TotalAmount.ToString("0.##"), o.Status, o.PaymentMethod ?? "", o.CreatedAt.ToString("u") }).ToList();
                    break;

                case "study-groups":
                    header = new[] { "Id", "Nom", "Matiere", "Code", "Actif", "ProprietaireEmail", "Membres", "Creation" };
                    rows = (await _db.StudyGroups.AsNoTracking()
                        .OrderBy(g => g.Id)
                        .Select(g => new { g.Id, g.Name, g.Subject, g.JoinCode, g.IsActive, email = g.Owner == null ? null : g.Owner.Email,
                                           members = _db.StudyGroupMembers.Count(m => m.StudyGroupId == g.Id), g.CreatedAt })
                        .ToListAsync())
                        .Select(g => new[] { g.Id.ToString(), g.Name, g.Subject ?? "", g.JoinCode, g.IsActive ? "oui" : "non",
                                             g.email ?? "", g.members.ToString(), g.CreatedAt.ToString("u") }).ToList();
                    break;

                case "teacher-classes":
                    header = new[] { "Id", "Nom", "Niveau", "Annee", "EnseignantEmail", "Eleves", "Creation" };
                    rows = (await _db.TeacherClasses.AsNoTracking()
                        .OrderBy(c => c.Id)
                        .Select(c => new { c.Id, c.Name, c.Level, c.AcademicYear, email = c.Teacher == null ? null : c.Teacher.Email,
                                           students = _db.TeacherClassStudents.Count(cs => cs.TeacherClassId == c.Id), c.CreatedAt })
                        .ToListAsync())
                        .Select(c => new[] { c.Id.ToString(), c.Name, c.Level ?? "", c.AcademicYear ?? "", c.email ?? "",
                                             c.students.ToString(), c.CreatedAt.ToString("u") }).ToList();
                    break;

                case "parent-credits":
                    header = new[] { "Id", "ParentEmail", "Type", "Montant", "EnfantId", "CommandeId", "Libelle", "Periode", "Date" };
                    rows = (await _db.ParentCreditLedgers.AsNoTracking()
                        .OrderByDescending(l => l.CreatedAt)
                        .Select(l => new { l.Id, email = l.Parent == null ? null : l.Parent.Email, l.EntryType, l.Amount,
                                           l.ChildId, l.OrderId, l.Label, l.PeriodStart, l.CreatedAt })
                        .ToListAsync())
                        .Select(l => new[] { l.Id.ToString(), l.email ?? "", l.EntryType, l.Amount.ToString("0.##"),
                                             l.ChildId?.ToString() ?? "", l.OrderId?.ToString() ?? "", l.Label ?? "",
                                             l.PeriodStart.ToString("yyyy-MM"), l.CreatedAt.ToString("u") }).ToList();
                    break;

                case "institution-licences":
                    header = new[] { "Id", "Institution", "Ville", "Pays", "ElevesRattaches", "ElevesActifs" };
                    rows = (await _db.Institutions.AsNoTracking().Where(i => !i.IsDeleted)
                        .OrderBy(i => i.Id)
                        .Select(i => new { i.Id, i.Name, i.City, i.Country,
                                           all = _db.InstitutionStudents.Count(s => s.InstitutionId == i.Id),
                                           act = _db.InstitutionStudents.Count(s => s.InstitutionId == i.Id && s.IsActive) })
                        .ToListAsync())
                        .Select(i => new[] { i.Id.ToString(), i.Name, i.City ?? "", i.Country, i.all.ToString(), i.act.ToString() }).ToList();
                    break;

                default:
                    return BadRequest(new { success = false, error = "Registre inconnu." });
            }

            var sb = new StringBuilder();
            sb.Append('\uFEFF');                       // BOM : Excel lit correctement les accents
            sb.AppendLine(string.Join(';', header));
            foreach (var row in rows)
                sb.AppendLine(string.Join(';', row.Select(Escape)));

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var name = $"winplus-{registry}-{DateTime.UtcNow:yyyyMMdd}.csv";
            return File(bytes, "text/csv", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting registry {Registry}", registry);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    private static string Escape(string? value)
    {
        var s = value ?? "";
        return s.Contains(';') || s.Contains('"') || s.Contains('\n')
            ? '"' + s.Replace("\"", "\"\"") + '"'
            : s;
    }
}
