using Backend.Data;
using Backend.Models.Entities;

namespace Backend.Services;

/// <summary>
/// Pochette de document générée par WinAI à la demande (case « Générer une
/// pochette » à l'upload). Pas de génération automatique : appelé uniquement
/// par CoverController sur action explicite.
/// </summary>
public interface ICoverGenerationService
{
    /// <summary>Génère une pochette et l'assigne à l'entité (Subject, Exam ou Course).</summary>
    Task<CoverGenerationResult> GenerateAndAssignAsync(string kind, int id, CancellationToken ct = default);
}

public record CoverGenerationResult(bool Success, string? ThumbnailUrl, string? Error);

public class CoverGenerationService : ICoverGenerationService
{
    private readonly ApplicationDbContext _db;
    private readonly IFastApiClient _fastApi;
    private readonly IStorageService _storage;
    private readonly ILogger<CoverGenerationService> _logger;

    public CoverGenerationService(
        ApplicationDbContext db,
        IFastApiClient fastApi,
        IStorageService storage,
        ILogger<CoverGenerationService> logger)
    {
        _db = db;
        _fastApi = fastApi;
        _storage = storage;
        _logger = logger;
    }

    public async Task<CoverGenerationResult> GenerateAndAssignAsync(string kind, int id, CancellationToken ct = default)
    {
        string title;
        string? description;
        switch (kind)
        {
            case "subject":
                var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
                if (subject == null) return new(false, null, "Contenu introuvable.");
                title = subject.Title; description = subject.Description;
                break;
            case "exam":
                var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == id, ct);
                if (exam == null) return new(false, null, "Épreuve introuvable.");
                title = exam.Title; description = exam.Description;
                break;
            case "course":
                var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
                if (course == null) return new(false, null, "Formation introuvable.");
                title = course.Title; description = course.Description;
                break;
            default:
                return new(false, null, "Type de contenu inconnu.");
        }

        var response = await _fastApi.PostAsync<CoverApiResponse>("/api/ai/cover/generate",
            new { title, description, kind }, null);
        if (response == null || string.IsNullOrEmpty(response.ImageBase64))
        {
            _logger.LogWarning("Pochette {Kind}/{Id} : réponse vide de WinAI", kind, id);
            return new(false, null, "La génération de pochette est indisponible pour le moment.");
        }

        var bytes = Convert.FromBase64String(response.ImageBase64);
        var extension = response.MimeType == "image/jpeg" ? "jpg" : "png";
        var key = $"covers/{kind}/{id}-{Guid.NewGuid():N}.{extension}";
        using var stream = new MemoryStream(bytes);
        var url = await _storage.PutAsync(stream, key, response.MimeType ?? "image/png", ct);

        switch (kind)
        {
            case "subject":
                var s = await _db.Subjects.FirstAsync(x => x.Id == id, ct);
                s.ThumbnailUrl = url;
                break;
            case "exam":
                var e = await _db.Exams.FirstAsync(x => x.Id == id, ct);
                e.ThumbnailUrl = url;
                break;
            case "course":
                var c = await _db.Courses.FirstAsync(x => x.Id == id, ct);
                c.ThumbnailUrl = url;
                break;
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Pochette {Kind}/{Id} générée par {Model} et assignée", kind, id, response.Model);
        return new(true, url, null);
    }

    private sealed class CoverApiResponse
    {
        public string ImageBase64 { get; set; } = string.Empty;
        public string? MimeType { get; set; }
        public string? Model { get; set; }
    }
}
