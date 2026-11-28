using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.WinAi;

/// <summary>
/// Atelier WinAI (25/11/2026) : sources que l'utilisateur choisit pour une discussion.
///   document     : document personnel téléversé (<see cref="WinAiSource"/>), fichier dans le stockage privé,
///                  extraits dans le RAG personnel (filtre doc_id + owner_user_id côté Python) ;
///   epreuve      : épreuve du catalogue à laquelle il a accès (gratuite, achetée, abonnement) → subject_id ;
///   formation    : formation suivie (inscription active), gratuite, ou dont il est l'auteur → course_id ;
///   erreurs-quiz : ses questions ratées non encore résolues, envoyées en texte (pas de document indexé).
/// Le client n'envoie que des identifiants opaques {type, id} : chaque accès est contrôlé ici avant d'être
/// traduit en filtre RAG ; aucun doc_id brut ne vient jamais du client.
/// Les appels au service Python portent le jeton de l'utilisateur (comme les pièces jointes du chat).
/// </summary>
public interface IWinAiSourceService
{
    Task<SourcesUsage> UsageAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyList<WinAiSource>> ListAsync(int userId, string? bearer, CancellationToken ct = default);
    Task<WinAiSource> UploadAsync(int userId, IFormFile file, string? title, string? bearer, CancellationToken ct = default);
    Task<bool> DeleteAsync(int userId, int sourceId, string? bearer, CancellationToken ct = default);
    Task<AvailableSources> AvailableAsync(int userId, bool isAdmin, CancellationToken ct = default);
    Task<ResolvedSources> ResolveAsync(int userId, bool isAdmin, IReadOnlyList<SourceRef> refs, CancellationToken ct = default);
    Task PurgeUserAsync(int userId, string? bearer, CancellationToken ct = default);
}

public sealed record SourceRef(string Type, int Id);

public sealed record SourcesUsage(int Files, long Bytes, long MaxFiles, long MaxBytes);

public sealed record AvailableItem(string Type, int Id, string Title, string? Detail);

public sealed record AvailableSources(IReadOnlyList<AvailableItem> Documents, IReadOnlyList<AvailableItem> Epreuves,
    IReadOnlyList<AvailableItem> Formations, AvailableItem? ErreursQuiz);

/// <summary>Sources traduites pour le service Python, et correspondance inverse pour les citations.</summary>
public sealed class ResolvedSources
{
    public List<string> DocIds { get; } = new();
    public List<int> SubjectIds { get; } = new();
    public List<int> CourseIds { get; } = new();
    public string? ContextText { get; set; }
    public Dictionary<string, (string Type, int Id, string Title)> ByDocId { get; } = new();
    public Dictionary<int, (string Type, int Id, string Title)> BySubjectId { get; } = new();
    public Dictionary<int, (string Type, int Id, string Title)> ByCourseId { get; } = new();
    public bool IsEmpty => DocIds.Count == 0 && SubjectIds.Count == 0 && CourseIds.Count == 0 && string.IsNullOrEmpty(ContextText);

    /// <summary>Corps « sources_filter » attendu par le service Python (null si rien à rechercher).</summary>
    public object? Filter => DocIds.Count == 0 && SubjectIds.Count == 0 && CourseIds.Count == 0 ? null
        : new { doc_ids = DocIds, subject_ids = SubjectIds, course_ids = CourseIds };

    /// <summary>Citations du service Python ({doc_id, subject_id, course_id, title, page}) → {type, id, title, page} du client.</summary>
    public List<object> MapCitations(JsonElement citations)
    {
        var res = new List<object>();
        if (citations.ValueKind != JsonValueKind.Array) return res;
        var vues = new HashSet<string>();
        foreach (var c in citations.EnumerateArray())
        {
            (string Type, int Id, string Title)? cible = null;
            if (c.TryGetProperty("doc_id", out var d) && d.ValueKind == JsonValueKind.String && ByDocId.TryGetValue(d.GetString()!, out var x)) cible = x;
            else if (c.TryGetProperty("subject_id", out var s) && s.ValueKind == JsonValueKind.Number && BySubjectId.TryGetValue(s.GetInt32(), out var y)) cible = y;
            else if (c.TryGetProperty("course_id", out var k) && k.ValueKind == JsonValueKind.Number && ByCourseId.TryGetValue(k.GetInt32(), out var z)) cible = z;
            if (cible is not { } t) continue;   // extrait hors des sources choisies : jamais montré
            int? page = c.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
            if (!vues.Add($"{t.Type}:{t.Id}:{page}")) continue;
            res.Add(new { type = t.Type, id = t.Id, title = t.Title, page });
        }
        return res;
    }
}

public class WinAiSourceException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public WinAiSourceException(int status, string code, string message) : base(message) { Status = status; Code = code; }
}

public class WinAiSourceService : IWinAiSourceService
{
    public const int MaxSourcesPerMessage = 10;
    /// <summary>25 Mo par fichier (5 Mo avant le 28/11/2026, trop peu pour un cours ou une épreuve scannés) ;
    /// sous la limite de requête de l'API (32 Mo) et de nginx (50 Mo). L'espace total reste borné par ai.sources.max_mb.</summary>
    public const long MaxFileBytes = 25L * 1024 * 1024;
    /// <summary>Types acceptés (mêmes familles que les pièces jointes du site) et extension envoyée au RAG.</summary>
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["text/plain"] = ".txt",
        ["text/markdown"] = ".md",
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
    };
    private static readonly Dictionary<string, string> TypeParExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".txt"] = "text/plain", [".md"] = "text/markdown", [".png"] = "image/png", [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg", [".webp"] = "image/webp",
    };
    /// <summary>Sans extraits après ce délai, une indexation est considérée comme échouée.</summary>
    private static readonly TimeSpan DelaiIndexation = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly ApplicationDbContext _db;
    private readonly IPrivateFileStore _files;
    private readonly IEntitlementService _entitlements;
    private readonly IContentAccessService _access;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<WinAiSourceService> _logger;

    public WinAiSourceService(ApplicationDbContext db, IPrivateFileStore files, IEntitlementService entitlements,
        IContentAccessService access, IHttpClientFactory http, ILogger<WinAiSourceService> logger)
    {
        _db = db; _files = files; _entitlements = entitlements; _access = access; _http = http; _logger = logger;
    }

    // ───────────────────────── Usage et plafonds ─────────────────────────

    public async Task<SourcesUsage> UsageAsync(int userId, CancellationToken ct = default)
    {
        var ent = await _entitlements.GetAsync(userId, ct);
        var maxFiles = ent.Int(EntitlementCatalog.AiSourcesMaxFiles);
        var maxMb = ent.Int(EntitlementCatalog.AiSourcesMaxMb);
        var used = await _db.WinAiSources.AsNoTracking().Where(s => s.UserId == userId)
            .GroupBy(_ => 1).Select(g => new { N = g.Count(), B = g.Sum(s => s.SizeBytes) }).FirstOrDefaultAsync(ct);
        return new SourcesUsage(used?.N ?? 0, used?.B ?? 0,
            maxFiles, maxMb == EntitlementCatalog.Unlimited ? EntitlementCatalog.Unlimited : maxMb * 1024 * 1024);
    }

    // ───────────────────────── Liste (avec mise à jour de l'état) ─────────────────────────

    public async Task<IReadOnlyList<WinAiSource>> ListAsync(int userId, string? bearer, CancellationToken ct = default)
    {
        var liste = await _db.WinAiSources.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var aVerifier = liste.Where(s => s.Status == WinAiSourceStatuses.Pending
            && (s.CheckedAt == null || now - s.CheckedAt > TimeSpan.FromSeconds(10))).Take(10).ToList();
        if (aVerifier.Count > 0)
        {
            foreach (var s in aVerifier) await RafraichirAsync(s, bearer, now, ct);
            await _db.SaveChangesAsync(ct);
        }
        return liste;
    }

    private async Task RafraichirAsync(WinAiSource s, string? bearer, DateTime now, CancellationToken ct)
    {
        s.CheckedAt = now;
        try
        {
            using var req = Requete(HttpMethod.Get, $"/api/rag/documents/{Uri.EscapeDataString(s.DocId)}/state", bearer);
            using var res = await Client().SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var chunks = doc.RootElement.TryGetProperty("chunks", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            var job = doc.RootElement.TryGetProperty("job_status", out var j) && j.ValueKind == JsonValueKind.String ? j.GetString() : null;
            var err = doc.RootElement.TryGetProperty("job_error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (chunks > 0)
            {
                s.Status = WinAiSourceStatuses.Ready; s.ChunkCount = chunks; s.IndexedAt = now; s.Error = null;
            }
            else if (job == "failed" || job == "done" || now - s.CreatedAt > DelaiIndexation)
            {
                // Terminé sans extrait (document vide, illisible) ou travail perdu (redémarrage du service).
                s.Status = WinAiSourceStatuses.Failed;
                s.Error = Tronquer(job == "failed" && !string.IsNullOrWhiteSpace(err) ? "Lecture du document impossible."
                    : "Aucun texte exploitable n'a été trouvé dans ce document.", 500);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sources WinAI : état de {DocId} indisponible", s.DocId);
        }
    }

    // ───────────────────────── Téléversement ─────────────────────────

    public async Task<WinAiSource> UploadAsync(int userId, IFormFile file, string? title, string? bearer, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0) throw new WinAiSourceException(400, "fichier_vide", "Choisissez un fichier.");
        if (file.Length > MaxFileBytes) throw new WinAiSourceException(413, "fichier_trop_gros", $"Ce fichier dépasse {MaxFileBytes / (1024 * 1024)} Mo.");
        var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        var contentType = (file.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!Extensions.ContainsKey(contentType) && TypeParExtension.TryGetValue(ext, out var deduit)) contentType = deduit;
        if (!Extensions.TryGetValue(contentType, out var extRag))
            throw new WinAiSourceException(415, "type_non_pris_en_charge", "Formats acceptés : PDF, Word (.docx), texte, images PNG, JPEG ou WebP.");

        var usage = await UsageAsync(userId, ct);
        if (usage.MaxFiles != EntitlementCatalog.Unlimited && usage.Files >= usage.MaxFiles)
            throw new WinAiSourceException(409, "plafond_fichiers", usage.MaxFiles == 0
                ? "Votre abonnement ne comprend pas de sources personnelles."
                : $"Vous avez atteint {usage.MaxFiles} documents : supprimez-en un ou passez à un abonnement supérieur.");
        if (usage.MaxBytes != EntitlementCatalog.Unlimited && usage.Bytes + file.Length > usage.MaxBytes)
            throw new WinAiSourceException(409, "plafond_volume",
                $"Espace plein ({usage.MaxBytes / (1024 * 1024)} Mo) : supprimez un document ou passez à un abonnement supérieur.");

        byte[] contenu;
        await using (var flux = file.OpenReadStream())
        using (var ms = new MemoryStream())
        {
            await flux.CopyToAsync(ms, ct);
            contenu = ms.ToArray();
        }

        var guid = Guid.NewGuid().ToString("N");
        var docId = $"winsrc_{userId}_{guid}";
        var nomFichier = Tronquer(Path.GetFileName(file.FileName ?? $"document{extRag}"), 255);
        string storageRef;
        using (var ms = new MemoryStream(contenu))
            storageRef = await _files.PutAsync(ms, $"private/winai-sources/{userId}/{guid}{extRag}", contentType, ct);

        var source = new WinAiSource
        {
            UserId = userId,
            Title = Tronquer(string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(nomFichier) : title.Trim(), 200),
            FileName = nomFichier, ContentType = contentType, SizeBytes = contenu.LongLength,
            StorageRef = storageRef, DocId = docId, Status = WinAiSourceStatuses.Pending,
        };
        _db.WinAiSources.Add(source);
        await _db.SaveChangesAsync(ct);

        // Indexation dans le RAG personnel (le service Python pose owner_user_id depuis le jeton).
        try
        {
            using var req = Requete(HttpMethod.Post, "/api/rag/ingest", bearer);
            req.Content = JsonContent.Create(new
            {
                doc_id = docId, title = source.Title, file_path = "",
                inline_content_base64 = Convert.ToBase64String(contenu), file_extension_hint = extRag,
            });
            using var res = await Client().SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                source.Status = WinAiSourceStatuses.Failed;
                source.Error = "Le service WinAI n'a pas pu lire ce document. Réessayez plus tard.";
                _logger.LogWarning("Sources WinAI : ingestion refusée {Status} pour {DocId}", (int)res.StatusCode, docId);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            source.Status = WinAiSourceStatuses.Failed;
            source.Error = "Le service WinAI est indisponible. Supprimez ce document et réessayez plus tard.";
            _logger.LogWarning(ex, "Sources WinAI : ingestion impossible pour {DocId}", docId);
            await _db.SaveChangesAsync(ct);
        }
        return source;
    }

    // ───────────────────────── Suppression ─────────────────────────

    public async Task<bool> DeleteAsync(int userId, int sourceId, string? bearer, CancellationToken ct = default)
    {
        var s = await _db.WinAiSources.FirstOrDefaultAsync(x => x.Id == sourceId && x.UserId == userId, ct);
        if (s == null) return false;
        await SupprimerAsync(s, bearer, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task SupprimerAsync(WinAiSource s, string? bearer, CancellationToken ct)
    {
        // Extraits du RAG d'abord : un échec ici laisse la ligne en place pour une nouvelle tentative.
        using (var req = Requete(HttpMethod.Delete, $"/api/rag/documents/{Uri.EscapeDataString(s.DocId)}", bearer))
        using (var res = await Client().SendAsync(req, ct))
        {
            if (!res.IsSuccessStatusCode)
                throw new WinAiSourceException(503, "suppression_indisponible", "Suppression impossible pour l'instant. Réessayez dans quelques minutes.");
        }
        try { await _files.DeleteAsync(s.StorageRef, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Sources WinAI : fichier {Ref} non supprimé", s.StorageRef); }
        _db.WinAiSources.Remove(s);
    }

    public async Task PurgeUserAsync(int userId, string? bearer, CancellationToken ct = default)
    {
        var liste = await _db.WinAiSources.Where(s => s.UserId == userId).ToListAsync(ct);
        foreach (var s in liste)
        {
            try { await SupprimerAsync(s, bearer, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Sources WinAI : purge de {DocId} incomplète", s.DocId); }
        }
        await _db.SaveChangesAsync(ct);
    }

    // ───────────────────────── Sources disponibles ─────────────────────────

    public async Task<AvailableSources> AvailableAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        var documents = await _db.WinAiSources.AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == WinAiSourceStatuses.Ready)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new AvailableItem("document", s.Id, s.Title, s.FileName)).ToListAsync(ct);

        // Épreuves : achetées, en favori, ou payées par un parent lié ; seulement celles auxquelles l'accès est réel.
        var achetees = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.SubjectId != null && oi.Order.UserId == userId && PaidOrderStatus.All.Contains(oi.Order.Status.ToLower()))
            .Select(oi => oi.SubjectId!.Value).ToListAsync(ct);
        var favoris = await _db.Favorites.AsNoTracking().Where(f => f.UserId == userId).Select(f => f.SubjectId).ToListAsync(ct);
        var candidats = achetees.Concat(favoris).Distinct().ToList();
        var sujets = await _db.Subjects.Where(s => candidats.Contains(s.Id) && !s.IsDeleted).ToListAsync(ct);
        var epreuves = new List<AvailableItem>();
        foreach (var s in sujets.OrderBy(s => s.Title))
            if (await AccesEpreuveAsync(userId, s, isAdmin)) epreuves.Add(new AvailableItem("epreuve", s.Id, s.Title, s.Category));

        var formations = await _db.CourseEnrollments.AsNoTracking()
            .Where(e => e.UserId == userId && e.IsActive)
            .OrderByDescending(e => e.LastAccessedAt ?? e.EnrolledAt)
            .Select(e => new AvailableItem("formation", e.CourseId, e.Course.Title, e.Course.Category)).ToListAsync(ct);

        var nbErreurs = await _db.QuizMistakes.AsNoTracking().CountAsync(m => m.UserId == userId && !m.IsResolved, ct);
        var erreurs = nbErreurs > 0 ? new AvailableItem("erreurs-quiz", 0, "Mes erreurs de quiz", $"{nbErreurs} question(s) à revoir") : null;

        return new AvailableSources(documents, epreuves, formations.DistinctBy(f => f.Id).ToList(), erreurs);
    }

    private async Task<bool> AccesEpreuveAsync(int userId, Subject s, bool isAdmin) =>
        (s.Price <= 0 && s.IsPublished) || await _access.HasPaidContentAccessAsync(userId, s, isAdmin);

    // ───────────────────────── Traduction pour le chat ─────────────────────────

    public async Task<ResolvedSources> ResolveAsync(int userId, bool isAdmin, IReadOnlyList<SourceRef> refs, CancellationToken ct = default)
    {
        var res = new ResolvedSources();
        if (refs == null || refs.Count == 0) return res;
        if (refs.Count > MaxSourcesPerMessage)
            throw new WinAiSourceException(400, "trop_de_sources", $"Choisissez au plus {MaxSourcesPerMessage} sources pour une question.");

        foreach (var r in refs.DistinctBy(r => (r.Type?.ToLowerInvariant(), r.Id)))
        {
            switch (r.Type?.ToLowerInvariant())
            {
                case "document":
                {
                    var s = await _db.WinAiSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.Id && x.UserId == userId, ct)
                        ?? throw Refus(r);
                    if (s.Status != WinAiSourceStatuses.Ready)
                        throw new WinAiSourceException(409, "source_pas_prete", $"« {s.Title} » est encore en cours de lecture par WinAI.");
                    res.DocIds.Add(s.DocId);
                    res.ByDocId[s.DocId] = ("document", s.Id, s.Title);
                    break;
                }
                case "epreuve":
                {
                    var s = await _db.Subjects.FirstOrDefaultAsync(x => x.Id == r.Id && !x.IsDeleted, ct);
                    if (s == null || !await AccesEpreuveAsync(userId, s, isAdmin)) throw Refus(r);
                    res.SubjectIds.Add(s.Id);
                    res.BySubjectId[s.Id] = ("epreuve", s.Id, s.Title);
                    break;
                }
                case "formation":
                {
                    var c = await _db.Courses.AsNoTracking().Where(x => x.Id == r.Id)
                        .Select(x => new { x.Id, x.Title, x.InstructorId, x.IsFree, x.Price }).FirstOrDefaultAsync(ct) ?? throw Refus(r);
                    var acces = isAdmin || c.InstructorId == userId || c.IsFree || c.Price <= 0
                        || await _db.CourseEnrollments.AnyAsync(e => e.CourseId == c.Id && e.UserId == userId && e.IsActive, ct);
                    if (!acces) throw Refus(r);
                    res.CourseIds.Add(c.Id);
                    res.ByCourseId[c.Id] = ("formation", c.Id, c.Title);
                    break;
                }
                case "erreurs-quiz":
                {
                    var erreurs = await _db.QuizMistakes.AsNoTracking()
                        .Where(m => m.UserId == userId && !m.IsResolved)
                        .OrderByDescending(m => m.CreatedAt).Take(15)
                        .Select(m => new { m.Subject, m.Question, m.GivenAnswer, m.CorrectAnswer }).ToListAsync(ct);
                    if (erreurs.Count > 0)
                    {
                        var sb = new StringBuilder("Questions de quiz ratées récemment par l'élève (à revoir) :\n");
                        foreach (var e in erreurs)
                            sb.Append("- ").Append(string.IsNullOrWhiteSpace(e.Subject) ? "" : $"[{e.Subject}] ")
                              .Append(Tronquer(e.Question, 300)).Append(" — sa réponse : ").Append(Tronquer(e.GivenAnswer ?? "aucune", 120))
                              .Append(" ; bonne réponse : ").Append(Tronquer(e.CorrectAnswer ?? "?", 120)).Append('\n');
                        res.ContextText = Tronquer(sb.ToString(), 5800);
                    }
                    break;
                }
                default:
                    throw new WinAiSourceException(400, "source_inconnue", $"Type de source inconnu : {r.Type}.");
            }
        }
        return res;
    }

    private static WinAiSourceException Refus(SourceRef r) =>
        new(403, "source_inaccessible", "Une des sources choisies n'existe pas ou ne vous est pas accessible.");

    // ───────────────────────── Outils ─────────────────────────

    private HttpClient Client() => _http.CreateClient("FastApiClient");

    private static HttpRequestMessage Requete(HttpMethod methode, string chemin, string? bearer)
    {
        var req = new HttpRequestMessage(methode, chemin);
        if (!string.IsNullOrWhiteSpace(bearer)) req.Headers.TryAddWithoutValidation("Authorization", bearer);
        return req;
    }

    private static string Tronquer(string? s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max];
}
