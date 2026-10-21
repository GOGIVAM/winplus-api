using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Translation;

/// <summary>Type de contenu traduisible, tel qu'il circule dans la file et les URL d'administration.</summary>
public static class ContentKinds
{
    public const string Subject = "subject";
    public const string Exam = "exam";
    public const string Course = "course";
    public const string ConcoursEvent = "concours";

    public static readonly string[] All = { Subject, Exam, Course, ConcoursEvent };
    public static bool IsValid(string? kind) => kind != null && All.Contains(kind);
}

public sealed record ContentRef(string Kind, int Id);

public sealed record TranslationOutcome(string Kind, int Id, string State, int TokensUsed, string? Error = null,
    Dictionary<string, object?>? Suggestion = null);

/// <summary>Un champ traduisible : un texte, ou une liste de textes (tags, objectifs…).</summary>
internal sealed record TextField(string Name, List<string> Fr, Action<List<string>?> SetEn, bool IsList = false);

/// <summary>
/// Traduction automatique des contenus de la base et règles de cycle de vie.
///
/// Règles :
///  - une ligne dont <c>TranslationOrigin = human</c> n'est jamais écrasée : si
///    son français change, elle passe à l'état « review » (à revoir) ;
///  - une ligne « auto » est retraduite dès que son français change (empreinte
///    <c>TranslationSourceHash</c>) ;
///  - un échec (WinAI indisponible, quota) laisse l'état « failed » : le contenu
///    reste servi en français et la tâche planifiée réessaie ;
///  - les libellés de taxonomie inconnus (matière, type d'examen, niveau…) sont
///    traduits et ajoutés en « auto, à relire ».
/// </summary>
public sealed class ContentTranslationService
{
    private readonly ApplicationDbContext _db;
    private readonly IContentTextTranslator _translator;
    private readonly IContentTranslationIndex _index;
    private readonly ILogger<ContentTranslationService> _logger;

    public ContentTranslationService(ApplicationDbContext db, IContentTextTranslator translator,
        IContentTranslationIndex index, ILogger<ContentTranslationService> logger)
    {
        _db = db;
        _translator = translator;
        _index = index;
        _logger = logger;
    }

    // ── Champs et empreintes ────────────────────────────────────────────────

    private static List<string> One(string? v) => string.IsNullOrWhiteSpace(v) ? new List<string>() : new List<string> { v };
    private static List<string> Many(List<string>? v) => v?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
    private static string? First(List<string>? v) => v is { Count: > 0 } ? v[0] : null;

    internal static List<TextField> FieldsOf(ITranslatableContent row) => row switch
    {
        Subject s => new()
        {
            new("titleEn", One(s.Title), v => s.TitleEn = First(v)),
            new("descriptionEn", One(s.Description), v => s.DescriptionEn = First(v)),
        },
        Exam e => new()
        {
            new("titleEn", One(e.Title), v => e.TitleEn = First(v)),
            new("descriptionEn", One(e.Description), v => e.DescriptionEn = First(v)),
        },
        Course c => new()
        {
            new("titleEn", One(c.Title), v => c.TitleEn = First(v)),
            new("shortDescriptionEn", One(c.ShortDescription), v => c.ShortDescriptionEn = First(v)),
            new("descriptionEn", One(c.Description), v => c.DescriptionEn = First(v)),
            new("tagsEn", Many(c.Tags), v => c.TagsEn = v, true),
            new("requirementsEn", Many(c.Requirements), v => c.RequirementsEn = v, true),
            new("objectivesEn", Many(c.Objectives), v => c.ObjectivesEn = v, true),
        },
        CourseSection s => new()
        {
            new("titleEn", One(s.Title), v => s.TitleEn = First(v)),
            new("descriptionEn", One(s.Description), v => s.DescriptionEn = First(v)),
        },
        CourseLesson l => new()
        {
            new("titleEn", One(l.Title), v => l.TitleEn = First(v)),
            new("descriptionEn", One(l.Description), v => l.DescriptionEn = First(v)),
        },
        _ => new(),
    };

    public static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Empreinte des champs français d'une ligne : change dès qu'un texte source change.</summary>
    public static string SourceHash(ITranslatableContent row) =>
        Sha(string.Join("␞", FieldsOf(row).Select(f => f.Name + "=" + string.Join("␟", f.Fr))));

    /// <summary>
    /// À l'enregistrement d'un contenu dont le français a changé (appelé par
    /// <see cref="ContentTranslationSaveInterceptor"/>, dans la même sauvegarde).
    /// Renvoie vrai si une traduction est à faire.
    /// </summary>
    public static bool MarkSourceChanged(ITranslatableContent row)
    {
        var hash = SourceHash(row);
        if (row.TranslationSourceHash == hash && row.TranslationState is TranslationStates.Ok) return false;
        if (row.TranslationOrigin == TranslationOrigins.Human)
        {
            // Jamais écrasée : seulement signalée « à revoir ».
            if (row.TranslationSourceHash != hash) row.TranslationState = TranslationStates.Review;
            return false;
        }
        row.TranslationState = TranslationStates.Pending;
        return true;
    }

    /// <summary>Taxonomie portée par un contenu : (type, valeur française).</summary>
    internal static IEnumerable<(string Kind, string Value)> TaxonomyOf(object row)
    {
        IEnumerable<(string, string?)> raw = row switch
        {
            Subject s => new (string, string?)[] { ("category", s.Category) }
                .Concat((s.Level ?? "").Split(',').Select(l => ("level", (string?)l))),
            Exam e => new (string, string?)[] { ("examType", e.ExamType), ("category", e.Category), ("level", e.Level), ("session", e.Session) },
            // Course.Level est un code (debutant, intermediaire, avance) traduit par l'interface.
            Course c => new (string, string?)[] { ("category", c.Category) },
            _ => Array.Empty<(string, string?)>(),
        };
        return raw.Where(x => !string.IsNullOrWhiteSpace(x.Item2)).Select(x => (x.Item1, x.Item2!.Trim()));
    }

    // ── Glossaire ───────────────────────────────────────────────────────────

    private async Task<List<(string Fr, string En)>> GlossaryAsync(CancellationToken ct)
    {
        var glossary = ContentGlossary.Terms.ToList();
        try
        {
            var rows = await _db.ContentTranslations.AsNoTracking()
                .Where(t => t.Origin == TranslationOrigins.Human || !t.NeedsReview)
                .Select(t => new { t.ValueFr, t.ValueEn }).ToListAsync(ct);
            glossary.AddRange(rows.Where(r => r.ValueFr != r.ValueEn).Select(r => (r.ValueFr, r.ValueEn)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Glossaire de taxonomie indisponible");
        }
        return glossary.GroupBy(g => g.Item1, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    // ── Taxonomie ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ajoute les libellés de taxonomie encore inconnus, traduits
    /// automatiquement et marqués « à relire ». Un échec est sans effet (reprise
    /// au prochain enregistrement ou passage de la tâche planifiée).
    /// </summary>
    public async Task<int> EnsureTaxonomyAsync(IEnumerable<(string Kind, string Value)> values, CancellationToken ct = default)
    {
        var wanted = values.Where(v => v.Value.Length <= 300)
            .GroupBy(v => (v.Kind, v.Value.ToLowerInvariant())).Select(g => g.First()).ToList();
        if (wanted.Count == 0) return 0;

        var existing = await _db.ContentTranslations.AsNoTracking().Select(t => new { t.Kind, t.ValueFr }).ToListAsync(ct);
        var known = existing.Select(e => (e.Kind, e.ValueFr.Trim().ToLowerInvariant())).ToHashSet();
        var missing = wanted.Where(w => !known.Contains((w.Kind, w.Value.ToLowerInvariant()))).ToList();
        if (missing.Count == 0) return 0;

        // Valeur déjà connue sous un autre type (« Mathématiques » matière d'un
        // livre et d'une épreuve) : même traduction, sans appel à WinAI.
        var anyKind = await _db.ContentTranslations.AsNoTracking().ToListAsync(ct);
        var toTranslate = new List<string>();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in missing)
        {
            var same = anyKind.FirstOrDefault(t => string.Equals(t.ValueFr.Trim(), m.Value, StringComparison.OrdinalIgnoreCase));
            if (same != null) resolved[m.Value] = same.ValueEn;
            else toTranslate.Add(m.Value);
        }
        if (toTranslate.Count > 0)
        {
            var result = await _translator.TranslateAsync(toTranslate, await GlossaryAsync(ct), ct);
            foreach (var (fr, en) in result.Translations) resolved[fr] = en;
            if (!result.Success) _logger.LogInformation("Taxonomie : traduction partielle ({Error})", result.Error);
        }

        var added = 0;
        foreach (var m in missing)
        {
            if (!resolved.TryGetValue(m.Value, out var en) || string.IsNullOrWhiteSpace(en)) continue;
            _db.ContentTranslations.Add(new ContentTranslation
            {
                Kind = m.Kind, ValueFr = m.Value, ValueEn = en.Trim(),
                Origin = TranslationOrigins.Auto, NeedsReview = true,
            });
            added++;
        }
        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _index.Invalidate();
        }
        return added;
    }

    // ── Contenus ────────────────────────────────────────────────────────────

    /// <summary>
    /// Traduit un contenu (une formation entraîne ses chapitres et leçons).
    /// <paramref name="force"/> : retraduit même si le français n'a pas changé
    /// (bouton « Retraduire ») ; une traduction humaine n'est pas modifiée pour
    /// autant : la proposition est renvoyée dans <see cref="TranslationOutcome.Suggestion"/>.
    /// <paramref name="onlyPublished"/> : la tâche de fond ne traduit pas les brouillons.
    /// </summary>
    public async Task<TranslationOutcome> TranslateAsync(ContentRef target, bool force = false, bool onlyPublished = true,
        CancellationToken ct = default)
    {
        if (target.Kind == ContentKinds.ConcoursEvent) return await TranslateConcoursEventAsync(target.Id, force, ct);

        var (main, children, published, taxonomy) = await LoadAsync(target, ct);
        if (main == null) return new TranslationOutcome(target.Kind, target.Id, "missing", 0, "Contenu introuvable");
        if (onlyPublished && !published) return new TranslationOutcome(target.Kind, target.Id, main.TranslationState ?? "", 0, "Non publié");

        await EnsureTaxonomyAsync(taxonomy, ct);

        var rows = new List<ITranslatableContent> { main };
        rows.AddRange(children);

        var todo = new List<ITranslatableContent>();
        var humanSuggestions = new List<ITranslatableContent>();
        foreach (var row in rows)
        {
            var hash = SourceHash(row);
            if (row.TranslationOrigin == TranslationOrigins.Human)
            {
                if (row.TranslationSourceHash != hash && row.TranslationState != TranslationStates.Review)
                    row.TranslationState = TranslationStates.Review;
                if (force && ReferenceEquals(row, main)) humanSuggestions.Add(row);
                continue;
            }
            if (!force && row.TranslationState == TranslationStates.Ok && row.TranslationSourceHash == hash) continue;
            todo.Add(row);
        }

        var texts = todo.Concat(humanSuggestions).SelectMany(r => FieldsOf(r).SelectMany(f => f.Fr)).ToList();
        var result = texts.Count == 0
            ? new TranslationBatchResult(true, new Dictionary<string, string>(), 0, null)
            : await _translator.TranslateAsync(texts, await GlossaryAsync(ct), ct);

        var now = DateTime.UtcNow;
        foreach (var row in todo)
        {
            var fields = FieldsOf(row);
            if (fields.All(f => f.Fr.All(t => result.Translations.ContainsKey(t))))
            {
                foreach (var f in fields)
                    f.SetEn(f.Fr.Count == 0 ? null : f.Fr.Select(t => result.Translations[t]).ToList());
                row.TranslationOrigin = TranslationOrigins.Auto;
                row.TranslationState = TranslationStates.Ok;
                row.TranslationSourceHash = SourceHash(row);
                row.TranslatedAt = now;
            }
            else
            {
                row.TranslationState = TranslationStates.Failed;
            }
        }

        Dictionary<string, object?>? suggestion = null;
        if (humanSuggestions.Count > 0)
        {
            suggestion = new Dictionary<string, object?>();
            foreach (var f in FieldsOf(main))
            {
                var en = f.Fr.Select(t => result.Translations.TryGetValue(t, out var x) ? x : null).ToList();
                suggestion[f.Name] = f.IsList ? en : en.FirstOrDefault();
            }
        }

        await _db.SaveChangesAsync(ct);
        _index.Invalidate();
        var state = main.TranslationState ?? TranslationStates.Pending;
        if (!result.Success) _logger.LogInformation("Traduction {Kind} {Id} : {Error}", target.Kind, target.Id, result.Error);
        return new TranslationOutcome(target.Kind, target.Id, state, result.TokensUsed, result.Success ? null : result.Error, suggestion);
    }

    private async Task<(ITranslatableContent? main, List<ITranslatableContent> children, bool published, List<(string, string)> taxonomy)>
        LoadAsync(ContentRef target, CancellationToken ct)
    {
        switch (target.Kind)
        {
            case ContentKinds.Subject:
            {
                var s = await _db.Subjects.FirstOrDefaultAsync(x => x.Id == target.Id && !x.IsDeleted, ct);
                return (s, new(), s?.IsPublished == true, s == null ? new() : TaxonomyOf(s).ToList());
            }
            case ContentKinds.Exam:
            {
                var e = await _db.Exams.FirstOrDefaultAsync(x => x.Id == target.Id && !x.IsDeleted, ct);
                return (e, new(), e?.IsPublished == true, e == null ? new() : TaxonomyOf(e).ToList());
            }
            case ContentKinds.Course:
            {
                var c = await _db.Courses.FirstOrDefaultAsync(x => x.Id == target.Id, ct);
                if (c == null) return (null, new(), false, new());
                var children = new List<ITranslatableContent>();
                children.AddRange(await _db.CourseSections.Where(s => s.CourseId == c.Id).OrderBy(s => s.Position).ToListAsync(ct));
                children.AddRange(await _db.CourseLessons.Where(l => l.CourseId == c.Id).OrderBy(l => l.Position).ToListAsync(ct));
                return (c, children, c.Status == "published", TaxonomyOf(c).ToList());
            }
            default:
                return (null, new(), false, new());
        }
    }

    /// <summary>
    /// Enregistre une version anglaise saisie à la main (écran « English ») :
    /// elle passe en « human » et n'est plus jamais écrasée par WinAI.
    /// Clés acceptées : titleEn, descriptionEn, shortDescriptionEn, tagsEn,
    /// requirementsEn, objectivesEn ; pour une formation, sections[] et lessons[]
    /// ({ id, titleEn, descriptionEn }).
    /// </summary>
    public async Task<bool> SaveHumanAsync(ContentRef target, JsonObject body, CancellationToken ct = default)
    {
        var (main, children, _, _) = await LoadAsync(target, ct);
        if (main == null) return false;
        var now = DateTime.UtcNow;
        ApplyHuman(main, body, now);
        foreach (var key in new[] { "sections", "lessons" })
        {
            if (body[key] is not JsonArray arr) continue;
            foreach (var item in arr.OfType<JsonObject>())
            {
                var id = item["id"]?.GetValue<int>();
                var row = children.FirstOrDefault(c => c.Id == id && (key == "sections" ? c is CourseSection : c is CourseLesson));
                if (row != null) ApplyHuman(row, item, now);
            }
        }
        await _db.SaveChangesAsync(ct);
        _index.Invalidate();
        return true;
    }

    private static void ApplyHuman(ITranslatableContent row, JsonObject body, DateTime now)
    {
        var touched = false;
        foreach (var f in FieldsOf(row))
        {
            if (!body.ContainsKey(f.Name)) continue;
            touched = true;
            var node = body[f.Name];
            if (f.IsList)
                f.SetEn(node is JsonArray a ? a.Select(x => x?.ToString()?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList() : null);
            else
                f.SetEn(node?.ToString().Trim() is { Length: > 0 } t ? new List<string> { t } : null);
        }
        if (!touched) return;
        row.TranslationOrigin = TranslationOrigins.Human;
        row.TranslationState = TranslationStates.Ok;
        row.TranslationSourceHash = SourceHash(row);
        row.TranslatedAt = now;
    }

    // ── Concours ────────────────────────────────────────────────────────────

    internal static readonly (string Fr, string En)[] ConcoursFields =
    {
        ("Name", "NameEn"), ("Notes", "NotesEn"), ("Tips", "TipsEn"), ("FaqJson", "FaqJsonEn"),
        ("Programme", "ProgrammeEn"), ("AdmissionConditions", "AdmissionConditionsEn"),
        ("Tracks", "TracksEn"), ("Coefficients", "CoefficientsEn"),
    };

    private static string? GetProp(ConcoursEvent ev, string name) => (string?)typeof(ConcoursEvent).GetProperty(name)!.GetValue(ev);
    private static void SetProp(ConcoursEvent ev, string name, string? value) => typeof(ConcoursEvent).GetProperty(name)!.SetValue(ev, value);

    /// <summary>Textes à traduire d'un champ français (une FAQ JSON donne ses questions et réponses).</summary>
    internal static List<string> ConcoursTexts(string field, string fr)
    {
        if (field != "FaqJson") return new List<string> { fr };
        try
        {
            var arr = JsonNode.Parse(fr) as JsonArray;
            return arr?.OfType<JsonObject>()
                .SelectMany(o => new[] { o["question"]?.ToString(), o["answer"]?.ToString() })
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList() ?? new List<string>();
        }
        catch (JsonException) { return new List<string>(); }
    }

    internal static string? ConcoursRebuild(string field, string fr, IReadOnlyDictionary<string, string> tr)
    {
        if (field != "FaqJson") return tr.TryGetValue(fr, out var en) ? en : null;
        var arr = JsonNode.Parse(fr) as JsonArray;
        if (arr == null) return null;
        var output = new JsonArray();
        foreach (var o in arr.OfType<JsonObject>())
        {
            var q = o["question"]?.ToString() ?? "";
            var a = o["answer"]?.ToString() ?? "";
            if (!tr.TryGetValue(q, out var qe) || !tr.TryGetValue(a, out var ae)) return null;
            output.Add(new JsonObject { ["question"] = qe, ["answer"] = ae });
        }
        return output.ToJsonString();
    }

    /// <summary>
    /// Concours : chaque champ *En vide (ou rempli par WinAI et dont le français
    /// a changé) est traduit ; un champ *En saisi à la main n'est jamais touché,
    /// il est seulement signalé « à revoir » si son français change.
    /// </summary>
    public async Task<TranslationOutcome> TranslateConcoursEventAsync(int id, bool force = false, CancellationToken ct = default)
    {
        var ev = await _db.ConcoursEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev == null) return new TranslationOutcome(ContentKinds.ConcoursEvent, id, "missing", 0, "Concours introuvable");
        var meta = ConcoursTranslationMeta.Parse(ev.TranslationMetaJson);
        meta.Review.Clear();

        var todo = new List<(string Fr, string En, string Source)>();
        foreach (var (frName, enName) in ConcoursFields)
        {
            var fr = GetProp(ev, frName);
            var en = GetProp(ev, enName);
            if (string.IsNullOrWhiteSpace(fr))
            {
                if (meta.Auto.Remove(enName)) SetProp(ev, enName, null);
                meta.Human.Remove(enName);
                continue;
            }
            var hash = Sha(fr);
            if (meta.Human.TryGetValue(enName, out var humanHash))
            {
                if (humanHash != hash) meta.Review.Add(enName);
                continue;
            }
            var isAuto = meta.Auto.TryGetValue(enName, out var autoHash);
            if (!isAuto && !string.IsNullOrWhiteSpace(en))
            {
                // Version anglaise antérieure au suivi : saisie à la main.
                meta.Human[enName] = hash;
                continue;
            }
            if (force || string.IsNullOrWhiteSpace(en) || autoHash != hash) todo.Add((frName, enName, fr));
        }

        var texts = todo.SelectMany(t => ConcoursTexts(t.Fr, t.Source)).ToList();
        var result = texts.Count == 0
            ? new TranslationBatchResult(true, new Dictionary<string, string>(), 0, null)
            : await _translator.TranslateAsync(texts, await GlossaryAsync(ct), ct);

        var failed = false;
        foreach (var (frName, enName, source) in todo)
        {
            var rebuilt = ConcoursRebuild(frName, source, result.Translations);
            if (rebuilt == null) { failed = true; continue; }
            SetProp(ev, enName, rebuilt);
            meta.Auto[enName] = Sha(source);
        }
        meta.Pending = failed;
        meta.Failed = failed;
        ev.TranslationMetaJson = meta.ToJson();
        await _db.SaveChangesAsync(ct);
        var state = failed ? TranslationStates.Failed : meta.Review.Count > 0 ? TranslationStates.Review : TranslationStates.Ok;
        return new TranslationOutcome(ContentKinds.ConcoursEvent, id, state, result.TokensUsed, result.Success ? null : result.Error);
    }

    /// <summary>
    /// Bouton « Traduire » de l'écran admin des concours : propose une version
    /// anglaise des champs français fournis, sans rien enregistrer.
    /// </summary>
    public async Task<(Dictionary<string, string?> Fields, string? Error)> PreviewConcoursAsync(IReadOnlyDictionary<string, string?> fr, CancellationToken ct = default)
    {
        var fields = ConcoursFields.Where(f => fr.TryGetValue(f.Fr, out var v) && !string.IsNullOrWhiteSpace(v)).ToList();
        var texts = fields.SelectMany(f => ConcoursTexts(f.Fr, fr[f.Fr]!)).ToList();
        var result = await _translator.TranslateAsync(texts, await GlossaryAsync(ct), ct);
        var output = new Dictionary<string, string?>();
        foreach (var (frName, enName) in fields)
            output[enName] = ConcoursRebuild(frName, fr[frName]!, result.Translations);
        return (output, result.Success ? null : result.Error);
    }

    // ── Rattrapage ──────────────────────────────────────────────────────────

    /// <summary>
    /// Contenus publiés sans traduction à jour (jamais traduits, en échec, en
    /// attente), traductions humaines exclues. Sert à l'endpoint admin
    /// translate-missing (marque « pending ») et à son estimation.
    /// </summary>
    public async Task<(List<ContentRef> Refs, int Chars)> FindMissingAsync(bool includeNeverRequested, CancellationToken ct = default)
    {
        bool Wanted(string? state, string? origin) =>
            origin != TranslationOrigins.Human
            && (state == TranslationStates.Pending || state == TranslationStates.Failed || (includeNeverRequested && state == null));

        var refs = new List<ContentRef>();
        var chars = 0;

        var subjects = await _db.Subjects.AsNoTracking().Where(s => !s.IsDeleted && s.IsPublished)
            .Select(s => new { s.Id, s.TranslationState, s.TranslationOrigin, Len = s.Title.Length + (s.Description ?? "").Length }).ToListAsync(ct);
        foreach (var s in subjects.Where(s => Wanted(s.TranslationState, s.TranslationOrigin))) { refs.Add(new(ContentKinds.Subject, s.Id)); chars += s.Len; }

        var exams = await _db.Exams.AsNoTracking().Where(e => !e.IsDeleted && e.IsPublished)
            .Select(e => new { e.Id, e.TranslationState, e.TranslationOrigin, Len = e.Title.Length + (e.Description ?? "").Length }).ToListAsync(ct);
        foreach (var e in exams.Where(e => Wanted(e.TranslationState, e.TranslationOrigin))) { refs.Add(new(ContentKinds.Exam, e.Id)); chars += e.Len; }

        var courses = await _db.Courses.AsNoTracking().Where(c => c.Status == "published")
            .Select(c => new { c.Id, c.TranslationState, c.TranslationOrigin, c.Title, c.ShortDescription, c.Description, c.Tags, c.Requirements, c.Objectives })
            .ToListAsync(ct);
        var courseIds = courses.Select(c => c.Id).ToList();
        var childStates = await _db.CourseSections.AsNoTracking().Where(s => courseIds.Contains(s.CourseId))
            .Select(s => new { s.CourseId, s.TranslationState, s.TranslationOrigin, Len = s.Title.Length + (s.Description ?? "").Length })
            .Concat(_db.CourseLessons.AsNoTracking().Where(l => courseIds.Contains(l.CourseId))
                .Select(l => new { l.CourseId, l.TranslationState, l.TranslationOrigin, Len = l.Title.Length + (l.Description ?? "").Length }))
            .ToListAsync(ct);
        foreach (var c in courses)
        {
            var kids = childStates.Where(k => k.CourseId == c.Id).ToList();
            if (!Wanted(c.TranslationState, c.TranslationOrigin) && !kids.Any(k => Wanted(k.TranslationState, k.TranslationOrigin))) continue;
            refs.Add(new(ContentKinds.Course, c.Id));
            chars += c.Title.Length + (c.ShortDescription ?? "").Length + (c.Description ?? "").Length
                + c.Tags.Sum(t => t.Length) + c.Requirements.Sum(t => t.Length) + c.Objectives.Sum(t => t.Length) + kids.Sum(k => k.Len);
        }

        var events = await _db.ConcoursEvents.AsNoTracking().ToListAsync(ct);
        foreach (var ev in events)
        {
            var meta = ConcoursTranslationMeta.Parse(ev.TranslationMetaJson);
            var gaps = ConcoursFields.Where(f => !string.IsNullOrWhiteSpace(GetProp(ev, f.Fr)) && string.IsNullOrWhiteSpace(GetProp(ev, f.En))
                                                 && !meta.Human.ContainsKey(f.En)).ToList();
            if (gaps.Count == 0 || !(meta.Pending || meta.Failed || includeNeverRequested)) continue;
            refs.Add(new(ContentKinds.ConcoursEvent, ev.Id));
            chars += gaps.Sum(g => GetProp(ev, g.Fr)!.Length);
        }
        return (refs, chars);
    }

    /// <summary>
    /// Estimation en tokens d'une traduction, calée sur l'essai local du
    /// 21/10/2026 (5 contenus, 3 991 tokens réels) : environ 650 tokens fixes
    /// par contenu (consignes, glossaire, raisonnement du modèle), plus le texte
    /// lui-même en entrée et en sortie (~4 caractères par token), avec 30 % de marge.
    /// </summary>
    public static int EstimateTokens(int chars, int items) =>
        (int)Math.Ceiling(items * 650 + chars / 4.0 * 2 * 1.3);
}

/// <summary>Origine des champs *En d'un concours (colonne ConcoursEvents.TranslationMetaJson).</summary>
public sealed class ConcoursTranslationMeta
{
    public Dictionary<string, string> Auto { get; set; } = new();
    public Dictionary<string, string> Human { get; set; } = new();
    public List<string> Review { get; set; } = new();
    public bool Pending { get; set; }
    public bool Failed { get; set; }

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ConcoursTranslationMeta Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<ConcoursTranslationMeta>(json, Options) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Valeurs *En d'un concours avant modification (pour <see cref="ApplyAdminWrite"/>).</summary>
    public static Dictionary<string, string?> Snapshot(ConcoursEvent ev) =>
        ContentTranslationService.ConcoursFields.ToDictionary(f => f.En, f => (string?)typeof(ConcoursEvent).GetProperty(f.En)!.GetValue(ev));

    /// <summary>
    /// Enregistrement depuis l'écran admin des concours : un champ *En modifié
    /// à la main devient « human » ; un champ rempli par le bouton « Traduire »
    /// et non retouché (listé dans <paramref name="autoFields"/>) reste « auto ».
    /// </summary>
    public static void ApplyAdminWrite(ConcoursEvent ev, IReadOnlyDictionary<string, string?> before, IEnumerable<string>? autoFields)
    {
        var meta = Parse(ev.TranslationMetaJson);
        var auto = new HashSet<string>(autoFields ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (frName, enName) in ContentTranslationService.ConcoursFields)
        {
            var fr = (string?)typeof(ConcoursEvent).GetProperty(frName)!.GetValue(ev);
            var en = (string?)typeof(ConcoursEvent).GetProperty(enName)!.GetValue(ev);
            before.TryGetValue(enName, out var old);
            var hash = ContentTranslationService.Sha(fr ?? "");
            if (string.IsNullOrWhiteSpace(en))
            {
                meta.Auto.Remove(enName);
                meta.Human.Remove(enName);
                continue;
            }
            if (auto.Contains(enName) || auto.Contains(frName))
            {
                meta.Human.Remove(enName);
                meta.Auto[enName] = hash;
            }
            else if (!string.Equals(en, old, StringComparison.Ordinal))
            {
                meta.Auto.Remove(enName);
                meta.Human[enName] = hash;
                meta.Review.Remove(enName);
            }
        }
        ev.TranslationMetaJson = meta.ToJson();
    }
}
