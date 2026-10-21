using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Translation;

/// <summary>
/// Vue en mémoire des traductions : texte français → texte anglais (contenus
/// libres) et libellé français → libellé anglais (taxonomie). Construite à
/// partir de la base, immuable ; l'index la reconstruit après chaque
/// traduction ou correction (et au plus tard toutes les 10 minutes).
/// </summary>
public sealed class ContentTranslationSnapshot
{
    private readonly Dictionary<string, string> _text;
    private readonly HashSet<string> _knownFr;
    private readonly Dictionary<string, Dictionary<string, string>> _taxonomyByKind;
    private readonly Dictionary<string, string> _taxonomyAnyKind;
    private readonly Dictionary<string, HashSet<string>> _frenchByEnglish;

    public static readonly ContentTranslationSnapshot Empty = Build(Array.Empty<(string, string?)>(), Array.Empty<ContentTranslation>());

    private ContentTranslationSnapshot(Dictionary<string, string> text, HashSet<string> knownFr,
        Dictionary<string, Dictionary<string, string>> taxonomyByKind, Dictionary<string, string> taxonomyAnyKind,
        Dictionary<string, HashSet<string>> frenchByEnglish)
    {
        _text = text;
        _knownFr = knownFr;
        _taxonomyByKind = taxonomyByKind;
        _taxonomyAnyKind = taxonomyAnyKind;
        _frenchByEnglish = frenchByEnglish;
    }

    public int TextCount => _text.Count;
    public int TaxonomyCount => _taxonomyAnyKind.Count;

    private static string Norm(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// <paramref name="texts"/> : (français, anglais ou null) de chaque champ de
    /// contenu publié. Un texte sans anglais est « connu » : la réponse le marque
    /// contentLang = "fr".
    /// </summary>
    public static ContentTranslationSnapshot Build(IEnumerable<(string Fr, string? En)> texts, IEnumerable<ContentTranslation> taxonomy)
    {
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        var frenchByEnglish = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (fr, en) in texts)
        {
            if (string.IsNullOrWhiteSpace(fr)) continue;
            known.Add(fr);
            if (!string.IsNullOrWhiteSpace(en) && !text.ContainsKey(fr)) text[fr] = en;
        }

        var byKind = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var anyKind = new Dictionary<string, string>(StringComparer.Ordinal);
        // Traductions humaines d'abord : en cas de doublon, elles l'emportent.
        foreach (var row in taxonomy.OrderBy(t => t.Origin == TranslationOrigins.Human ? 0 : 1))
        {
            if (string.IsNullOrWhiteSpace(row.ValueFr) || string.IsNullOrWhiteSpace(row.ValueEn)) continue;
            var key = Norm(row.ValueFr);
            if (!byKind.TryGetValue(row.Kind, out var map)) byKind[row.Kind] = map = new Dictionary<string, string>(StringComparer.Ordinal);
            map.TryAdd(key, row.ValueEn.Trim());
            anyKind.TryAdd(key, row.ValueEn.Trim());
            var enKey = Norm(row.ValueEn);
            if (!frenchByEnglish.TryGetValue(enKey, out var set)) frenchByEnglish[enKey] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(key);
        }
        foreach (var (fr, en) in text)
        {
            var enKey = Norm(en);
            if (!frenchByEnglish.TryGetValue(enKey, out var set)) frenchByEnglish[enKey] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(Norm(fr));
        }

        return new ContentTranslationSnapshot(text, known, byKind, anyKind, frenchByEnglish);
    }

    /// <summary>Traduction d'un texte de contenu (titre, description…), correspondance exacte.</summary>
    public bool TryText(string fr, out string en) => _text.TryGetValue(fr, out en!);

    /// <summary>Le texte est-il un contenu de la base (traduit ou non) ?</summary>
    public bool IsKnownFrench(string value) => _knownFr.Contains(value);

    /// <summary>Libellé de taxonomie, d'abord dans le type donné, sinon tous types confondus (casse ignorée).</summary>
    public bool TryTaxonomy(string? kind, string fr, out string en)
    {
        var key = Norm(fr);
        if (kind != null && _taxonomyByKind.TryGetValue(kind, out var map) && map.TryGetValue(key, out en!)) return true;
        return _taxonomyAnyKind.TryGetValue(key, out en!);
    }

    /// <summary>
    /// Valeurs françaises (en minuscules) à chercher en base pour un filtre ou
    /// une recherche saisis dans l'une ou l'autre langue : la valeur elle-même,
    /// plus toutes les valeurs françaises dont l'anglais lui correspond.
    /// « Mathematics » → { "mathematics", "mathématiques" }.
    /// </summary>
    public IReadOnlyList<string> FrenchEquivalents(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        var key = Norm(value);
        var list = new List<string> { key };
        if (_frenchByEnglish.TryGetValue(key, out var set)) list.AddRange(set.Where(s => s != key));
        return list;
    }
}

public interface IContentTranslationIndex
{
    Task<ContentTranslationSnapshot> GetAsync(CancellationToken ct = default);
    /// <summary>À appeler après toute écriture de traduction : la prochaine lecture recharge.</summary>
    void Invalidate();
}

public sealed class ContentTranslationIndex : IContentTranslationIndex
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ContentTranslationIndex> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ContentTranslationSnapshot? _snapshot;
    private DateTime _loadedAt;
    private int _version;
    private int _loadedVersion = -1;

    public ContentTranslationIndex(IServiceScopeFactory scopes, ILogger<ContentTranslationIndex> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Invalidate() => Interlocked.Increment(ref _version);

    public async Task<ContentTranslationSnapshot> GetAsync(CancellationToken ct = default)
    {
        var current = _snapshot;
        if (current != null && _loadedVersion == Volatile.Read(ref _version) && DateTime.UtcNow - _loadedAt < MaxAge) return current;

        await _lock.WaitAsync(ct);
        try
        {
            var version = Volatile.Read(ref _version);
            if (_snapshot != null && _loadedVersion == version && DateTime.UtcNow - _loadedAt < MaxAge) return _snapshot;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            _snapshot = await LoadAsync(db, ct);
            _loadedAt = DateTime.UtcNow;
            _loadedVersion = version;
            return _snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table absente (script SQL pas encore passé) ou base indisponible :
            // les réponses restent en français plutôt que d'échouer.
            _logger.LogWarning(ex, "Index des traductions indisponible : contenus servis en français");
            _snapshot ??= ContentTranslationSnapshot.Empty;
            _loadedAt = DateTime.UtcNow;
            _loadedVersion = Volatile.Read(ref _version);
            return _snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    public static async Task<ContentTranslationSnapshot> LoadAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var texts = new List<(string, string?)>();

        var subjects = await db.Subjects.AsNoTracking().Where(s => !s.IsDeleted && s.IsPublished)
            .Select(s => new { s.Title, s.TitleEn, s.Description, s.DescriptionEn }).ToListAsync(ct);
        foreach (var s in subjects) { texts.Add((s.Title, s.TitleEn)); if (s.Description != null) texts.Add((s.Description, s.DescriptionEn)); }

        var exams = await db.Exams.AsNoTracking().Where(e => !e.IsDeleted && e.IsPublished)
            .Select(e => new { e.Title, e.TitleEn, e.Description, e.DescriptionEn }).ToListAsync(ct);
        foreach (var e in exams) { texts.Add((e.Title, e.TitleEn)); if (e.Description != null) texts.Add((e.Description, e.DescriptionEn)); }

        var courses = await db.Courses.AsNoTracking().Where(c => c.Status == "published")
            .Select(c => new { c.Id, c.Title, c.TitleEn, c.ShortDescription, c.ShortDescriptionEn, c.Description, c.DescriptionEn,
                c.Tags, c.TagsEn, c.Requirements, c.RequirementsEn, c.Objectives, c.ObjectivesEn }).ToListAsync(ct);
        foreach (var c in courses)
        {
            texts.Add((c.Title, c.TitleEn));
            if (c.ShortDescription != null) texts.Add((c.ShortDescription, c.ShortDescriptionEn));
            if (c.Description != null) texts.Add((c.Description, c.DescriptionEn));
            AddList(texts, c.Tags, c.TagsEn);
            AddList(texts, c.Requirements, c.RequirementsEn);
            AddList(texts, c.Objectives, c.ObjectivesEn);
        }
        var courseIds = courses.Select(c => c.Id).ToList();
        var sections = await db.CourseSections.AsNoTracking().Where(s => courseIds.Contains(s.CourseId))
            .Select(s => new { s.Title, s.TitleEn, s.Description, s.DescriptionEn }).ToListAsync(ct);
        foreach (var s in sections) { texts.Add((s.Title, s.TitleEn)); if (s.Description != null) texts.Add((s.Description, s.DescriptionEn)); }
        var lessons = await db.CourseLessons.AsNoTracking().Where(l => courseIds.Contains(l.CourseId))
            .Select(l => new { l.Title, l.TitleEn, l.Description, l.DescriptionEn }).ToListAsync(ct);
        foreach (var l in lessons) { texts.Add((l.Title, l.TitleEn)); if (l.Description != null) texts.Add((l.Description, l.DescriptionEn)); }

        var taxonomy = await db.ContentTranslations.AsNoTracking().ToListAsync(ct);
        return ContentTranslationSnapshot.Build(texts, taxonomy);
    }

    private static void AddList(List<(string, string?)> texts, List<string>? fr, List<string>? en)
    {
        if (fr == null) return;
        for (var i = 0; i < fr.Count; i++)
            texts.Add((fr[i], en != null && en.Count == fr.Count ? en[i] : null));
    }
}
