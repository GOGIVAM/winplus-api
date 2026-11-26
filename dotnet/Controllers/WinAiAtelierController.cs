using System.Text.Json;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Backend.Services.WinAi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Atelier WinAI (25/11/2026) : productions générées à partir des sources choisies par l'élève.
///   POST   /api/winai/flashcards          {subject?, topic?, level?, count? (5-30, 12), sources?[{type,id}]}
///   GET    /api/winai/flashcards          liste (sans les cartes)
///   GET    /api/winai/flashcards/{id}     paquet complet
///   DELETE /api/winai/flashcards/{id}
///   POST   /api/winai/resume-audio        {subject?, topic?, level?, minutes? (1-5, 3) ou duree? (courte | longue), sources?}
///   GET    /api/winai/resume-audio, GET /api/winai/resume-audio/{id}, DELETE /api/winai/resume-audio/{id}
///   POST   /api/teacher/epreuves          {subject?, topic?, level?, examType? (interrogation | devoir | composition |
///          examen-blanc), durationMinutes? (15-300, 120), totalPoints? (5-100, 20), exercises? (1-6, 3), instructions?, sources?}
///   GET    /api/teacher/epreuves, GET /api/teacher/epreuves/{id}, DELETE /api/teacher/epreuves/{id}
///          Épreuve complète (sujet, barème par question, corrigé) : professeur ou admin, droit ai.feature.quiz_generation.
/// Fiches : droit ai.feature.flashcards (Standard et au-dessus). Résumé audio : inclus dans tous les plans.
/// Les deux sont débités en jetons WinAI (même budget que le chat). Le script audio est lu par la synthèse
/// vocale de l'appareil : aucun fichier audio n'est produit.
/// Refus : {success:false, error, code, message}, message en français prêt à afficher.
/// </summary>
[ApiController]
[Route("api/winai")]
[Authorize]
public class WinAiAtelierController : ControllerBase
{
    public const string KindFlashcards = "flashcards";
    public const string KindAudio = "resume-audio";
    public const string KindEpreuve = "epreuve";
    private static readonly HashSet<string> ExamTypes = new() { "interrogation", "devoir", "composition", "examen-blanc" };
    private const int MaxPerUser = 200;

    private readonly ApplicationDbContext _db;
    private readonly IWinAiSourceService _sources;
    private readonly IAiFeatureGate _gate;
    private readonly IFastApiClient _fastApi;
    private readonly ILogger<WinAiAtelierController> _logger;

    public WinAiAtelierController(ApplicationDbContext db, IWinAiSourceService sources, IAiFeatureGate gate,
        IFastApiClient fastApi, ILogger<WinAiAtelierController> logger)
    {
        _db = db; _sources = sources; _gate = gate; _fastApi = fastApi; _logger = logger;
    }

    public sealed class SourceRefBody
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
    }

    public class GenerateBody
    {
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? Level { get; set; }
        public List<SourceRefBody>? Sources { get; set; }
    }

    public sealed class FlashcardsBody : GenerateBody { public int? Count { get; set; } }
    public sealed class AudioBody : GenerateBody
    {
        public int? Minutes { get; set; }
        /// <summary>Raccourci : « courte » (1 minute, ~140 mots) ou « longue » (3 minutes, ~420 mots).</summary>
        public string? Duree { get; set; }
    }

    public sealed class EpreuveBody : GenerateBody
    {
        public string? ExamType { get; set; }
        public int? DurationMinutes { get; set; }
        public int? TotalPoints { get; set; }
        public int? Exercises { get; set; }
        public string? Instructions { get; set; }
    }

    private static ObjectResult Refus(int status, string code, string message) =>
        new(new { success = false, error = message, code, message }) { StatusCode = status };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ── Fiches recto verso ────────────────────────────────────────────────────

    [HttpPost("flashcards")]
    public async Task<IActionResult> GenererFiches([FromBody] FlashcardsBody body, CancellationToken ct)
    {
        var count = body.Count ?? 12;
        if (count is < 5 or > 30) return Refus(400, "nombre_invalide", "Choisissez entre 5 et 30 fiches.");
        return await GenererAsync(KindFlashcards, body, EntitlementCatalog.AiFlashcards, "/api/winai/flashcards/generate-content",
            extra: new Dictionary<string, object?> { ["count"] = count }, ct);
    }

    [HttpGet("flashcards")]
    public Task<IActionResult> ListeFiches(CancellationToken ct) => ListeAsync(KindFlashcards, ct);

    [HttpGet("flashcards/{id:int}")]
    public Task<IActionResult> Fiches(int id, CancellationToken ct) => DetailAsync(KindFlashcards, id, ct);

    [HttpDelete("flashcards/{id:int}")]
    public Task<IActionResult> SupprimerFiches(int id, CancellationToken ct) => SupprimerAsync(KindFlashcards, id, ct);

    // ── Résumé audio ──────────────────────────────────────────────────────────

    [HttpPost("resume-audio")]
    public async Task<IActionResult> GenererAudio([FromBody] AudioBody body, CancellationToken ct)
    {
        var minutes = body.Minutes ?? (body.Duree?.Trim().ToLowerInvariant() switch { "courte" => 1, "longue" => 3, _ => 3 });
        if (minutes is < 1 or > 5) return Refus(400, "duree_invalide", "Choisissez une durée de 1 à 5 minutes.");
        return await GenererAsync(KindAudio, body, null, "/api/winai/resume-audio/generate-content",
            extra: new Dictionary<string, object?> { ["minutes"] = minutes }, ct);
    }

    [HttpGet("resume-audio")]
    public Task<IActionResult> ListeAudio(CancellationToken ct) => ListeAsync(KindAudio, ct);

    [HttpGet("resume-audio/{id:int}")]
    public Task<IActionResult> Audio(int id, CancellationToken ct) => DetailAsync(KindAudio, id, ct);

    [HttpDelete("resume-audio/{id:int}")]
    public Task<IActionResult> SupprimerAudio(int id, CancellationToken ct) => SupprimerAsync(KindAudio, id, ct);

    // ── Épreuve complète pour l'enseignant ────────────────────────────────────

    private bool EstEnseignant() => User.HasRole("teacher") || User.IsAdmin();
    private static ObjectResult RefusRole() =>
        Refus(403, "role_enseignant", "La création d'épreuves est réservée aux enseignants.");

    [HttpPost("~/api/teacher/epreuves")]
    public async Task<IActionResult> GenererEpreuve([FromBody] EpreuveBody body, CancellationToken ct)
    {
        if (!EstEnseignant()) return RefusRole();
        var type = (body.ExamType ?? "devoir").Trim().ToLowerInvariant();
        if (!ExamTypes.Contains(type))
            return Refus(400, "type_invalide", "Type d'épreuve inconnu : interrogation, devoir, composition ou examen blanc.");
        var duree = body.DurationMinutes ?? 120;
        if (duree is < 15 or > 300) return Refus(400, "duree_invalide", "Choisissez une durée de 15 minutes à 5 heures.");
        var total = body.TotalPoints ?? 20;
        if (total is < 5 or > 100) return Refus(400, "bareme_invalide", "Le total des points doit être compris entre 5 et 100.");
        var exos = body.Exercises ?? 3;
        if (exos is < 1 or > 6) return Refus(400, "nombre_invalide", "Choisissez entre 1 et 6 exercices.");
        return await GenererAsync(KindEpreuve, body, EntitlementCatalog.AiQuizGeneration, "/api/teacher/epreuve/generate-content",
            extra: new Dictionary<string, object?>
            {
                ["exam_type"] = type, ["duration_minutes"] = duree, ["total_points"] = total, ["exercises"] = exos,
                ["instructions"] = Court(body.Instructions, 1000),
            }, ct);
    }

    [HttpGet("~/api/teacher/epreuves")]
    public async Task<IActionResult> ListeEpreuves(CancellationToken ct) =>
        EstEnseignant() ? await ListeAsync(KindEpreuve, ct) : RefusRole();

    [HttpGet("~/api/teacher/epreuves/{id:int}")]
    public async Task<IActionResult> Epreuve(int id, CancellationToken ct) =>
        EstEnseignant() ? await DetailAsync(KindEpreuve, id, ct) : RefusRole();

    [HttpDelete("~/api/teacher/epreuves/{id:int}")]
    public async Task<IActionResult> SupprimerEpreuve(int id, CancellationToken ct) =>
        EstEnseignant() ? await SupprimerAsync(KindEpreuve, id, ct) : RefusRole();

    // ── Commun ────────────────────────────────────────────────────────────────

    private static string? Court(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);

    private async Task<IActionResult> GenererAsync(string kind, GenerateBody body, string? featureKey, string endpoint,
        Dictionary<string, object?> extra, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var subject = Court(body.Subject, 150);
        var topic = Court(body.Topic, 200);
        var refs = (body.Sources ?? new()).Select(s => new SourceRef(s.Type, s.Id)).ToList();
        if (subject == null && topic == null && refs.Count == 0)
            return Refus(400, "materiel_manquant", "Choisissez au moins une source, une matière ou un chapitre.");

        if (await _db.WinAiProductions.CountAsync(p => p.UserId == userId && p.Kind == kind, ct) >= MaxPerUser)
            return Refus(409, "trop_de_productions", $"Vous avez déjà {MaxPerUser} éléments de ce type : supprimez-en avant d'en créer d'autres.");

        ResolvedSources? resolved = null;
        if (refs.Count > 0)
        {
            try { resolved = await _sources.ResolveAsync(userId, User.IsAdmin(), refs, ct); }
            catch (WinAiSourceException e) { return Refus(e.Status, e.Code, e.Message); }
        }

        var level = Court(body.Level, 100)
            ?? await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Level).FirstOrDefaultAsync(ct);

        var ticket = featureKey != null
            ? await _gate.BeginAsync(userId, featureKey, ct)
            : await _gate.BeginIncludedAsync(userId, kind, ct);
        if (!ticket.Allowed) return ticket.Refusal!;

        int status = 0;
        string? raw = null;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["user_id"] = userId, ["subject"] = subject, ["topic"] = topic, ["level"] = level,
                ["sources_filter"] = resolved?.Filter, ["sources_context"] = resolved?.ContextText,
            };
            foreach (var (k, v) in extra) payload[k] = v;
            (status, raw) = await _fastApi.PostRawJsonAsync(endpoint, payload);
        }
        finally
        {
            await _gate.CompleteAsync(ticket, served: status == 200);
        }

        if (status != 200 || raw == null)
        {
            _logger.LogWarning("Atelier WinAI {Kind} : HTTP {Status} {Body}", kind, status, raw?.Length > 300 ? raw[..300] : raw);
            return status == 422
                ? Refus(400, "materiel_manquant", "Choisissez au moins une source, une matière ou un chapitre.")
                : Refus(502, "generation_echouee", kind switch
                {
                    KindFlashcards => "WinAI n'a pas pu préparer les fiches. Réessayez dans un instant.",
                    KindEpreuve => "WinAI n'a pas pu préparer l'épreuve. Réessayez dans un instant.",
                    _ => "WinAI n'a pas pu préparer le résumé. Réessayez dans un instant.",
                });
        }

        using var doc = JsonDocument.Parse(raw);
        var r = doc.RootElement;
        var citations = resolved != null && r.TryGetProperty("citations", out var c) ? resolved.MapCitations(c) : new List<object>();
        string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        object content;
        int items;
        if (kind == KindFlashcards)
        {
            var cards = r.TryGetProperty("cards", out var cs) && cs.ValueKind == JsonValueKind.Array
                ? cs.EnumerateArray().Select(x => new
                {
                    front = x.TryGetProperty("front", out var f) ? f.GetString() : null,
                    back = x.TryGetProperty("back", out var b) ? b.GetString() : null,
                    hint = x.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null,
                }).Where(x => !string.IsNullOrWhiteSpace(x.front) && !string.IsNullOrWhiteSpace(x.back)).ToList<object>()
                : new List<object>();
            content = new { cards };
            items = cards.Count;
        }
        else if (kind == KindEpreuve)
        {
            var exercises = r.TryGetProperty("exercises", out var ex) && ex.ValueKind == JsonValueKind.Array ? ex.Clone() : default;
            items = exercises.ValueKind == JsonValueKind.Array
                ? exercises.EnumerateArray().Sum(e => e.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array ? q.GetArrayLength() : 0)
                : 0;
            int? Int(string name) => r.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;
            content = new
            {
                examType = Str("exam_type"), durationMinutes = Int("duration_minutes"), totalPoints = Int("total_points"),
                instructions = Str("instructions"),
                exercises = exercises.ValueKind == JsonValueKind.Array ? (object)exercises : Array.Empty<object>(),
            };
        }
        else
        {
            var script = Str("script") ?? "";
            var words = r.TryGetProperty("word_count", out var w) && w.TryGetInt32(out var wc) ? wc : script.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var seconds = r.TryGetProperty("estimated_seconds", out var s) && s.TryGetInt32(out var sc) ? sc : words * 60 / 140;
            content = new { script, wordCount = words, estimatedSeconds = seconds };
            items = words;
        }

        var production = new WinAiProduction
        {
            UserId = userId, Kind = kind,
            Title = Court(Str("title"), 200) ?? kind switch { KindFlashcards => "Fiches de révision", KindEpreuve => "Épreuve", _ => "Résumé audio" },
            Subject = Court(subject ?? Str("subject"), 150), Topic = topic, Level = Court(level, 100), ItemCount = items,
            ContentJson = JsonSerializer.Serialize(content, Json),
            SourcesJson = JsonSerializer.Serialize(new { chosen = refs.Select(x => new { type = x.Type, id = x.Id }), citations }, Json),
        };
        _db.WinAiProductions.Add(production);
        await _db.SaveChangesAsync(ct);
        return StatusCode(201, new { success = true, data = Vue(production, full: true) });
    }

    private static object Vue(WinAiProduction p, bool full)
    {
        using var content = JsonDocument.Parse(p.ContentJson);
        using var sources = JsonDocument.Parse(p.SourcesJson);
        var baseVue = new Dictionary<string, object?>
        {
            ["id"] = p.Id, ["kind"] = p.Kind, ["title"] = p.Title, ["subject"] = p.Subject, ["topic"] = p.Topic,
            ["level"] = p.Level, ["createdAt"] = p.CreatedAt,
            [p.Kind switch { KindFlashcards => "cardCount", KindEpreuve => "questionCount", _ => "wordCount" }] = p.ItemCount,
            ["sources"] = sources.RootElement.TryGetProperty("citations", out var cit) ? cit.Clone() : null,
            ["chosenSources"] = sources.RootElement.TryGetProperty("chosen", out var ch) ? ch.Clone() : null,
        };
        if (p.Kind == KindAudio && content.RootElement.TryGetProperty("estimatedSeconds", out var es))
            baseVue["estimatedSeconds"] = es.Clone();
        if (p.Kind == KindEpreuve)
            foreach (var name in new[] { "examType", "durationMinutes", "totalPoints" })
                if (content.RootElement.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null)
                    baseVue[name] = v.Clone();
        if (full)
            foreach (var prop in content.RootElement.EnumerateObject())
                baseVue[prop.Name] = prop.Value.Clone();
        return baseVue;
    }

    private async Task<IActionResult> ListeAsync(string kind, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var items = await _db.WinAiProductions.AsNoTracking()
            .Where(p => p.UserId == userId && p.Kind == kind)
            .OrderByDescending(p => p.CreatedAt).Take(MaxPerUser).ToListAsync(ct);
        return Ok(new { success = true, data = items.Select(p => Vue(p, full: false)) });
    }

    private async Task<IActionResult> DetailAsync(string kind, int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var p = await _db.WinAiProductions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && x.Kind == kind, ct);
        return p == null ? Refus(404, "introuvable", "Élément introuvable.") : Ok(new { success = true, data = Vue(p, full: true) });
    }

    private async Task<IActionResult> SupprimerAsync(string kind, int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var p = await _db.WinAiProductions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && x.Kind == kind, ct);
        if (p == null) return Refus(404, "introuvable", "Élément introuvable.");
        _db.WinAiProductions.Remove(p);
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }
}
