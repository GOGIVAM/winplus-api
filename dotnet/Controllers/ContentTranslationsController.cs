using System.Text.Json.Nodes;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services.Translation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Zone « English » des écrans d'édition (épreuve, livre, formation), pour
/// l'administrateur et pour le professeur auteur du contenu :
///
///   GET  /api/content-translations/{kind}/{id}              français + anglais, origine, état
///   PUT  /api/content-translations/{kind}/{id}              enregistre l'anglais (devient « human »)
///   POST /api/content-translations/{kind}/{id}/retranslate  traduction automatique immédiate
///
/// kind : subject (livre ou fiche d'épreuve), exam, course (avec chapitres et leçons).
/// Le professeur n'est jamais obligé d'y passer : la traduction automatique se
/// fait d'elle-même en tâche de fond à la publication.
/// </summary>
[ApiController]
[Route("api/content-translations")]
[Authorize]
public class ContentTranslationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ContentTranslationService _service;

    public ContentTranslationsController(ApplicationDbContext db, ContentTranslationService service)
    {
        _db = db;
        _service = service;
    }

    private async Task<bool> CanEditAsync(string kind, int id)
    {
        if (User.IsAdmin()) return true;
        int userId;
        try { userId = User.GetUserId(); } catch (UnauthorizedAccessException) { return false; }
        return kind switch
        {
            ContentKinds.Subject => await _db.Subjects.AnyAsync(s => s.Id == id && s.AuthorUserId == userId),
            ContentKinds.Exam => await _db.Exams.AnyAsync(e => e.Id == id && e.SubjectReference != null && e.SubjectReference.AuthorUserId == userId),
            ContentKinds.Course => await _db.Courses.AnyAsync(c => c.Id == id && c.InstructorId == userId),
            _ => false,
        };
    }

    private static object Meta(ITranslatableContent r) => new
    {
        origin = r.TranslationOrigin,
        state = r.TranslationState,
        translatedAt = r.TranslatedAt,
        // Le français a changé depuis la traduction (vrai aussi pour une ligne « auto » en attente).
        stale = r.TranslationSourceHash != null && r.TranslationSourceHash != ContentTranslationService.SourceHash(r),
    };

    [HttpGet("{kind}/{id:int}")]
    public async Task<IActionResult> Get(string kind, int id)
    {
        if (!ContentKinds.IsValid(kind) || kind == ContentKinds.ConcoursEvent) return BadRequest(new { error = "Type de contenu inconnu." });
        if (!await CanEditAsync(kind, id)) return NotFound(new { error = "Contenu introuvable." });

        switch (kind)
        {
            case ContentKinds.Subject:
            {
                var s = await _db.Subjects.AsNoTracking().FirstAsync(x => x.Id == id);
                var exam = await _db.Exams.AsNoTracking().Where(e => e.SubjectId == id && !e.IsDeleted)
                    .OrderByDescending(e => e.CreatedAt).Select(e => new { e.Id }).FirstOrDefaultAsync();
                return Ok(new
                {
                    kind, id, title = s.Title, description = s.Description, titleEn = s.TitleEn, descriptionEn = s.DescriptionEn,
                    examId = exam?.Id, meta = Meta(s),
                });
            }
            case ContentKinds.Exam:
            {
                var e = await _db.Exams.AsNoTracking().FirstAsync(x => x.Id == id);
                return Ok(new { kind, id, title = e.Title, description = e.Description, titleEn = e.TitleEn, descriptionEn = e.DescriptionEn, meta = Meta(e) });
            }
            default:
            {
                var c = await _db.Courses.AsNoTracking().FirstAsync(x => x.Id == id);
                var sections = await _db.CourseSections.AsNoTracking().Where(x => x.CourseId == id).OrderBy(x => x.Position).ToListAsync();
                var lessons = await _db.CourseLessons.AsNoTracking().Where(x => x.CourseId == id).OrderBy(x => x.Position).ToListAsync();
                return Ok(new
                {
                    kind, id,
                    title = c.Title, shortDescription = c.ShortDescription, description = c.Description,
                    tags = c.Tags, requirements = c.Requirements, objectives = c.Objectives,
                    titleEn = c.TitleEn, shortDescriptionEn = c.ShortDescriptionEn, descriptionEn = c.DescriptionEn,
                    tagsEn = c.TagsEn, requirementsEn = c.RequirementsEn, objectivesEn = c.ObjectivesEn,
                    meta = Meta(c),
                    sections = sections.Select(x => new { x.Id, x.Title, x.Description, x.TitleEn, x.DescriptionEn, meta = Meta(x) }),
                    lessons = lessons.Select(x => new { x.Id, x.SectionId, x.Title, x.Description, x.TitleEn, x.DescriptionEn, meta = Meta(x) }),
                });
            }
        }
    }

    [HttpPut("{kind}/{id:int}")]
    public async Task<IActionResult> Save(string kind, int id, [FromBody] JsonObject body)
    {
        if (!ContentKinds.IsValid(kind) || kind == ContentKinds.ConcoursEvent) return BadRequest(new { error = "Type de contenu inconnu." });
        if (!await CanEditAsync(kind, id)) return NotFound(new { error = "Contenu introuvable." });
        foreach (var key in new[] { "titleEn", "descriptionEn", "shortDescriptionEn" })
            if (body[key]?.ToString() is { Length: > 20000 }) return BadRequest(new { error = "Texte anglais trop long (20 000 caractères au plus)." });
        await _service.SaveHumanAsync(new ContentRef(kind, id), body);
        return await Get(kind, id);
    }

    [HttpPost("{kind}/{id:int}/retranslate")]
    public async Task<IActionResult> Retranslate(string kind, int id)
    {
        if (!ContentKinds.IsValid(kind) || kind == ContentKinds.ConcoursEvent) return BadRequest(new { error = "Type de contenu inconnu." });
        if (!await CanEditAsync(kind, id)) return NotFound(new { error = "Contenu introuvable." });
        var outcome = await _service.TranslateAsync(new ContentRef(kind, id), force: true, onlyPublished: false);
        if (outcome.Error != null && outcome.Suggestion == null && outcome.State == TranslationStates.Failed)
            return StatusCode(503, new { error = "La traduction automatique est indisponible pour le moment. Le contenu reste affiché en français ; réessayez plus tard.", detail = outcome.Error });
        return Ok(new
        {
            outcome.State, outcome.TokensUsed, outcome.Error,
            // Traduction humaine : rien n'a été écrasé, la proposition est renvoyée pour relecture.
            suggestion = outcome.Suggestion,
        });
    }
}
