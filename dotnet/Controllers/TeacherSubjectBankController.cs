using Backend.Data;
using Backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Banque de sujets de l'établissement : les contenus publiés (épreuves,
/// corrigés, livres) et les quiz des collègues affiliés au(x) même(s)
/// établissement(s) que l'enseignant, à réutiliser avec ses classes.
///
/// GET /api/teacher/bank?search=&amp;category=&amp;kind=subject|quiz&amp;page=
/// </summary>
[ApiController]
[Route("api/teacher/bank")]
[Authorize]
public class TeacherSubjectBankController : ControllerBase
{
    private const int PageSize = 24;
    private readonly ApplicationDbContext _db;
    public TeacherSubjectBankController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search, [FromQuery] string? category, [FromQuery] string? kind, [FromQuery] int page = 1)
    {
        if (User.GetUserRole() != "teacher") return Forbid();
        var me = User.GetUserId();
        page = Math.Max(1, page);

        var myInstitutions = await _db.InstitutionTeacherLinks.AsNoTracking()
            .Where(l => l.TeacherId == me && l.Status == "accepted").Select(l => l.InstitutionId).ToListAsync();
        if (myInstitutions.Count == 0)
            return Ok(new { data = new { attached = false, institutions = Array.Empty<object>(), colleagues = 0, items = Array.Empty<object>(), categories = Array.Empty<string>(), total = 0 }, success = true });

        var institutions = await _db.Institutions.AsNoTracking().Where(i => myInstitutions.Contains(i.Id)).Select(i => i.Name).ToListAsync();
        var colleagues = await _db.InstitutionTeacherLinks.AsNoTracking()
            .Where(l => myInstitutions.Contains(l.InstitutionId) && l.Status == "accepted" && l.TeacherId != me)
            .Select(l => l.TeacherId).Distinct().ToListAsync();
        var names = await _db.Users.AsNoTracking().Where(u => colleagues.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var q = search?.Trim().ToLower();
        var subjects = _db.Subjects.AsNoTracking()
            .Where(s => s.AuthorUserId != null && colleagues.Contains(s.AuthorUserId.Value) && s.IsPublished && !s.IsDeleted);
        var quizzes = _db.Quizzes.AsNoTracking()
            .Where(x => x.CreatedByUserId != null && colleagues.Contains(x.CreatedByUserId.Value) && !x.IsDeleted && x.IsPublished);
        if (!string.IsNullOrEmpty(q))
        {
            subjects = subjects.Where(s => s.Title.ToLower().Contains(q) || (s.Category != null && s.Category.ToLower().Contains(q)));
            quizzes = quizzes.Where(x => x.Title.ToLower().Contains(q) || x.Subject.ToLower().Contains(q));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            subjects = subjects.Where(s => s.Category == category);
            quizzes = quizzes.Where(x => x.Subject == category);
        }

        var subjectRows = kind == "quiz" ? new List<BankItem>() : await subjects
            .Select(s => new BankItem("subject", s.Id, s.Title, s.Category, s.Level, s.ThumbnailUrl, s.AuthorUserId!.Value, s.CreatedAt, s.DownloadCount ?? 0, s.Price))
            .ToListAsync();
        var quizRows = kind == "subject" ? new List<BankItem>() : await quizzes
            .Select(x => new BankItem("quiz", x.Id, x.Title, x.Subject, null, null, x.CreatedByUserId!.Value, x.CreatedAt, x.Attempts, 0))
            .ToListAsync();

        var all = subjectRows.Concat(quizRows).OrderByDescending(i => i.CreatedAt).ToList();
        var categories = all.Select(i => i.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
        var items = all.Skip((page - 1) * PageSize).Take(PageSize).Select(i => new
        {
            i.Kind, i.Id, i.Title, i.Category, i.Level, i.ThumbnailUrl, i.CreatedAt, uses = i.Uses, i.Price,
            author = names.GetValueOrDefault(i.AuthorId) ?? "Collègue",
        });

        return Ok(new
        {
            data = new { attached = true, institutions, colleagues = colleagues.Count, items, categories, total = all.Count, page, pageSize = PageSize },
            success = true,
        });
    }

    private record BankItem(string Kind, int Id, string Title, string? Category, string? Level, string? ThumbnailUrl, int AuthorId, DateTime CreatedAt, int Uses, decimal Price);
}
