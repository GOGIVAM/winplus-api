using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public class CreateRevisionNoteRequest
{
    public int SubjectId { get; set; }
    public string? Content { get; set; }
}

public class ToggleRevisionTagRequest
{
    public int SubjectId { get; set; }
    public string? Label { get; set; }
}

/// <summary>
/// Notes et tags de révision d'un élève sur une épreuve (S7-1).
/// GET    /api/revision-notes?subjectId=42
/// POST   /api/revision-notes
/// DELETE /api/revision-notes/{id}
/// POST   /api/revision-notes/tags/toggle
/// </summary>
[ApiController]
[Route("api/revision-notes")]
[Authorize]
public class RevisionNotesController : ControllerBase
{
    private static readonly string[] AllowedTags = { "À réviser", "Difficile", "Maîtrisé", "À acheter" };

    private readonly ApplicationDbContext _db;
    private readonly ILogger<RevisionNotesController> _logger;

    public RevisionNotesController(ApplicationDbContext db, ILogger<RevisionNotesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int subjectId)
    {
        try
        {
            if (subjectId <= 0) return BadRequest(new { success = false, error = "subjectId requis." });

            var userId = User.GetUserId();

            var notes = await _db.RevisionNotes
                .AsNoTracking()
                .Where(n => n.UserId == userId && n.SubjectId == subjectId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new { n.Id, n.Content, n.CreatedAt })
                .ToListAsync();

            var tags = await _db.RevisionTags
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.SubjectId == subjectId)
                .Select(t => t.Label)
                .ToListAsync();

            return Ok(new { data = new { notes, tags, availableTags = AllowedTags }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting revision notes");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRevisionNoteRequest request)
    {
        try
        {
            var content = (request.Content ?? "").Trim();
            if (content.Length == 0) return BadRequest(new { success = false, error = "La note est vide." });
            if (content.Length > 300) return BadRequest(new { success = false, error = "300 caractères maximum." });
            if (request.SubjectId <= 0) return BadRequest(new { success = false, error = "subjectId requis." });

            var userId = User.GetUserId();

            var exists = await _db.Subjects.AnyAsync(s => s.Id == request.SubjectId && !s.IsDeleted);
            if (!exists) return NotFound(new { success = false, error = "Épreuve introuvable." });

            var note = new RevisionNote { UserId = userId, SubjectId = request.SubjectId, Content = content };
            _db.RevisionNotes.Add(note);
            await _db.SaveChangesAsync();

            return Ok(new { data = new { note.Id, note.Content, note.CreatedAt }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating revision note");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        try
        {
            var userId = User.GetUserId();
            var note = await _db.RevisionNotes.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (note != null)
            {
                _db.RevisionNotes.Remove(note);
                await _db.SaveChangesAsync();
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting revision note {Id}", id);
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }

    /// <summary>Pose ou retire un tag. Renvoie la liste des tags actifs.</summary>
    [HttpPost("tags/toggle")]
    public async Task<IActionResult> ToggleTag([FromBody] ToggleRevisionTagRequest request)
    {
        try
        {
            var label = (request.Label ?? "").Trim();
            if (!AllowedTags.Contains(label))
                return BadRequest(new { success = false, error = "Tag inconnu." });
            if (request.SubjectId <= 0)
                return BadRequest(new { success = false, error = "subjectId requis." });

            var userId = User.GetUserId();

            var tag = await _db.RevisionTags.FirstOrDefaultAsync(
                t => t.UserId == userId && t.SubjectId == request.SubjectId && t.Label == label);

            if (tag == null)
                _db.RevisionTags.Add(new RevisionTag { UserId = userId, SubjectId = request.SubjectId, Label = label });
            else
                _db.RevisionTags.Remove(tag);

            await _db.SaveChangesAsync();

            var tags = await _db.RevisionTags
                .Where(t => t.UserId == userId && t.SubjectId == request.SubjectId)
                .Select(t => t.Label)
                .ToListAsync();

            return Ok(new { data = new { tags }, success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling revision tag");
            return StatusCode(500, new { success = false, error = "Internal server error" });
        }
    }
}
