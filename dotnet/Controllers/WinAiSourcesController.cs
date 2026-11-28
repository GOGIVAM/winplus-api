using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services.WinAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Atelier WinAI (25/11/2026) : sources de l'utilisateur pour ses discussions avec WinAI.
///   GET    /api/winai/sources              documents personnels (état d'indexation), usage et plafond
///   POST   /api/winai/sources              téléversement (multipart : file, title facultatif)
///   DELETE /api/winai/sources/{id}         retrait du fichier et de ses extraits du RAG
///   GET    /api/winai/sources/disponibles  tout ce qu'il peut cocher : documents prêts, épreuves accessibles,
///                                          formations suivies, ses erreurs de quiz
/// Les sources cochées s'envoient ensuite dans le champ « sources » de /api/chatbot/stream et /message.
/// Plafonds selon l'abonnement : droits ai.sources.max_files et ai.sources.max_mb.
/// </summary>
[ApiController]
[Route("api/winai/sources")]
[Authorize]
public class WinAiSourcesController : ControllerBase
{
    private readonly IWinAiSourceService _sources;

    public WinAiSourcesController(IWinAiSourceService sources) { _sources = sources; }

    private string? Bearer => Request.Headers["Authorization"].ToString() is { Length: > 0 } h ? h : null;

    private static object Vue(WinAiSource s) => new
    {
        id = s.Id, type = "document", title = s.Title, fileName = s.FileName, contentType = s.ContentType,
        sizeBytes = s.SizeBytes, status = s.Status, error = s.Error, chunks = s.ChunkCount,
        createdAt = s.CreatedAt, indexedAt = s.IndexedAt,
    };

    private static object Usage(SourcesUsage u) => new
    {
        files = u.Files, bytes = u.Bytes, maxFiles = u.MaxFiles, maxBytes = u.MaxBytes,
        maxFileBytes = WinAiSourceService.MaxFileBytes, maxSourcesPerMessage = WinAiSourceService.MaxSourcesPerMessage,
        acceptedTypes = new[] { "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "text/plain", "text/markdown", "image/png", "image/jpeg", "image/webp" },
    };

    private ObjectResult Erreur(WinAiSourceException e) =>
        StatusCode(e.Status, new { success = false, code = e.Code, error = e.Message });

    [HttpGet]
    public async Task<IActionResult> Liste(CancellationToken ct)
    {
        var me = User.GetUserId();
        var liste = await _sources.ListAsync(me, Bearer, ct);
        var usage = await _sources.UsageAsync(me, ct);
        return Ok(new { success = true, data = new { usage = Usage(usage), sources = liste.Select(Vue) } });
    }

    [HttpPost]
    [RequestSizeLimit(WinAiSourceService.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = WinAiSourceService.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Televerser([FromForm] IFormFile? file, [FromForm] string? title, CancellationToken ct)
    {
        try
        {
            var s = await _sources.UploadAsync(User.GetUserId(), file!, title, Bearer, ct);
            var usage = await _sources.UsageAsync(User.GetUserId(), ct);
            return StatusCode(201, new { success = true, data = new { source = Vue(s), usage = Usage(usage) } });
        }
        catch (WinAiSourceException e) { return Erreur(e); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Supprimer(int id, CancellationToken ct)
    {
        try
        {
            if (!await _sources.DeleteAsync(User.GetUserId(), id, Bearer, ct))
                return NotFound(new { success = false, code = "introuvable", error = "Document introuvable." });
            var usage = await _sources.UsageAsync(User.GetUserId(), ct);
            return Ok(new { success = true, data = new { usage = Usage(usage) } });
        }
        catch (WinAiSourceException e) { return Erreur(e); }
    }

    [HttpGet("disponibles")]
    public async Task<IActionResult> Disponibles(CancellationToken ct)
    {
        var d = await _sources.AvailableAsync(User.GetUserId(), User.IsAdmin(), ct);
        static object Item(AvailableItem i) => new { type = i.Type, id = i.Id, title = i.Title, detail = i.Detail };
        return Ok(new
        {
            success = true,
            data = new
            {
                documents = d.Documents.Select(Item),
                epreuves = d.Epreuves.Select(Item),
                formations = d.Formations.Select(Item),
                erreursQuiz = d.ErreursQuiz == null ? null : Item(d.ErreursQuiz),
                maxSourcesPerMessage = WinAiSourceService.MaxSourcesPerMessage,
            },
        });
    }
}
