using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Translation;

public sealed record TranslationBatchResult(
    bool Success,
    IReadOnlyDictionary<string, string> Translations,
    int TokensUsed,
    string? Error);

/// <summary>Traduction fr → en d'une liste de textes (tous les appels passent par ici).</summary>
public interface IContentTextTranslator
{
    Task<TranslationBatchResult> TranslateAsync(IReadOnlyCollection<string> texts,
        IReadOnlyCollection<(string Fr, string En)> glossary, CancellationToken ct = default);
}

/// <summary>
/// Traduction par WinAI (POST /api/winai/translate-batch, service Python).
///  - jeton technique « ai.content-translation » : aucun utilisateur n'est
///    débité, le coût est celui de la plateforme (tokens notés dans le cache) ;
///  - textes identiques regroupés, cache persistant (table TranslationCache) :
///    un texte déjà traduit ne repart jamais vers WinAI ;
///  - lots d'au plus <see cref="MaxItemsPerCall"/> textes et
///    <see cref="MaxCharsPerCall"/> caractères.
/// Un échec ne lève pas d'exception : le résultat porte Success = false et les
/// traductions déjà obtenues.
/// </summary>
public sealed class WinAiContentTranslator : IContentTextTranslator
{
    public const int MaxItemsPerCall = 25;
    public const int MaxCharsPerCall = 6000;

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceTokenProvider _serviceToken;
    private readonly ILogger<WinAiContentTranslator> _logger;

    public WinAiContentTranslator(ApplicationDbContext db, IHttpClientFactory httpFactory,
        IServiceTokenProvider serviceToken, ILogger<WinAiContentTranslator> logger)
    {
        _db = db;
        _httpFactory = httpFactory;
        _serviceToken = serviceToken;
        _logger = logger;
    }

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ContentGlossary.Version + "\n" + text))).ToLowerInvariant();

    public async Task<TranslationBatchResult> TranslateAsync(IReadOnlyCollection<string> texts,
        IReadOnlyCollection<(string Fr, string En)> glossary, CancellationToken ct = default)
    {
        var distinct = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (distinct.Count == 0) return new TranslationBatchResult(true, result, 0, null);

        var hashes = distinct.ToDictionary(t => t, Hash, StringComparer.Ordinal);
        var hashList = hashes.Values.ToList();
        var cached = await _db.TranslationCache.AsNoTracking()
            .Where(c => c.TargetLang == ContentLanguage.English && hashList.Contains(c.SourceHash))
            .Select(c => new { c.SourceHash, c.TranslatedText })
            .ToListAsync(ct);
        var byHash = cached.GroupBy(c => c.SourceHash).ToDictionary(g => g.Key, g => g.First().TranslatedText);
        foreach (var t in distinct)
            if (byHash.TryGetValue(hashes[t], out var en)) result[t] = en;

        var missing = distinct.Where(t => !result.ContainsKey(t)).ToList();
        var tokens = 0;
        string? error = null;
        foreach (var chunk in Chunk(missing))
        {
            var (ok, translated, used, err) = await CallWinAiAsync(chunk, glossary, ct);
            tokens += used;
            var share = translated.Count == 0 ? 0 : used / translated.Count;
            foreach (var (fr, en) in translated)
            {
                result[fr] = en;
                _db.TranslationCache.Add(new TranslationCacheEntry
                {
                    SourceHash = hashes[fr], SourceText = fr, TranslatedText = en,
                    TargetLang = ContentLanguage.English, TokensUsed = share,
                });
            }
            if (translated.Count > 0) await _db.SaveChangesAsync(ct);
            if (!ok) { error = err; break; }
        }

        return new TranslationBatchResult(error == null, result, tokens, error);
    }

    private static IEnumerable<List<string>> Chunk(List<string> texts)
    {
        var current = new List<string>();
        var chars = 0;
        foreach (var t in texts)
        {
            if (current.Count > 0 && (current.Count >= MaxItemsPerCall || chars + t.Length > MaxCharsPerCall))
            {
                yield return current;
                current = new List<string>();
                chars = 0;
            }
            current.Add(t);
            chars += t.Length;
        }
        if (current.Count > 0) yield return current;
    }

    private async Task<(bool ok, Dictionary<string, string> translated, int tokens, string? error)> CallWinAiAsync(
        List<string> chunk, IReadOnlyCollection<(string Fr, string En)> glossary, CancellationToken ct)
    {
        var joined = string.Join("\n", chunk);
        var payload = new
        {
            source_lang = "fr",
            target_lang = "en",
            rules = ContentGlossary.Rules,
            glossary = glossary
                .Where(g => joined.Contains(g.Fr, StringComparison.OrdinalIgnoreCase))
                .Select(g => new { fr = g.Fr, en = g.En }),
            items = chunk.Select((t, i) => new { id = i.ToString(), text = t }),
        };

        try
        {
            var client = _httpFactory.CreateClient("FastApiClient");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/winai/translate-batch") { Content = JsonContent.Create(payload) };
            req.Headers.TryAddWithoutValidation("Authorization", _serviceToken.CreateAuthorizationHeader(ServiceScopes.ContentTranslation));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(120));
            using var res = await client.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("translate-batch : WinAI a répondu {Status} : {Body}", (int)res.StatusCode, body.Length > 300 ? body[..300] : body);
                return (false, new(), 0, $"WinAI {(int)res.StatusCode}");
            }

            var root = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
            var tokens = root.TryGetProperty("tokens_used", out var tu) && tu.TryGetInt32(out var n) ? n : 0;
            var translated = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idEl) || !int.TryParse(idEl.ToString(), out var idx)) continue;
                    if (idx < 0 || idx >= chunk.Count) continue;
                    var text = item.TryGetProperty("text", out var tx) ? tx.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(text)) translated[chunk[idx]] = text.Trim();
                }
            }
            // Une réponse incomplète compte comme un échec : ce qui manque sera repris.
            if (translated.Count < chunk.Count)
                return (false, translated, tokens, $"WinAI : {chunk.Count - translated.Count} texte(s) sans traduction");
            return (true, translated, tokens, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "translate-batch : appel WinAI impossible");
            return (false, new(), 0, "WinAI indisponible");
        }
    }
}
