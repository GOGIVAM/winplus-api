using Microsoft.Extensions.Caching.Memory;

namespace Backend.Services;

/// <summary>
/// Identifie un aperçu découpé : le contenu, le type de document, le fichier
/// stocké ET sa version (ETag S3, à défaut la date de modification), et le
/// nombre de pages autorisé par le droit catalog.preview_pages. Un fichier
/// remplacé change de version : l'ancien aperçu n'est plus jamais servi.
/// </summary>
public sealed record PreviewPdfKey(int SubjectId, string Kind, string StorageKey, string FileVersion, int AllowedPages)
{
    public string CacheKey => $"preview-pdf:{SubjectId}:{Kind}:{StorageKey}:{FileVersion}:{AllowedPages}";
}

/// <summary>
/// Aperçu découpé, SANS filigrane (commun à tous les utilisateurs), avec le
/// nombre de pages servies et le nombre total de pages du document.
/// </summary>
public sealed record PreviewPdf(byte[] Bytes, int ServedPages, int TotalPages);

/// <summary>
/// Lot 9.7b (catalog.preview_pages) : cache de l'aperçu découpé. Sans lui,
/// chaque ouverture d'un aperçu téléchargeait le PDF complet depuis S3 puis le
/// redécoupait. Ce qui est mis en cache est le PDF découpé AVANT filigrane :
/// le filigrane nominatif est posé ensuite à chaque appel, pour l'utilisateur
/// courant, si bien que le cache ne peut jamais servir le filigrane d'un autre.
/// Durée et taille bornées (cache dédié, indépendant du IMemoryCache partagé).
/// </summary>
public interface IPreviewPdfCache
{
    /// <summary>
    /// Renvoie l'aperçu découpé pour <paramref name="key"/>, en le préparant
    /// au besoin à partir du document complet fourni par
    /// <paramref name="loadSource"/> (appelé seulement en cas d'absence).
    /// Lève <see cref="PdfPreviewUnavailableException"/> si le document est
    /// trop court pour un aperçu.
    /// </summary>
    Task<PreviewPdf> GetOrCreateAsync(PreviewPdfKey key, Func<CancellationToken, Task<byte[]>> loadSource, CancellationToken ct = default);
}

public sealed class PreviewPdfCacheOptions
{
    /// <summary>Taille totale maximale du cache, en octets (64 Mo par défaut).</summary>
    public long MaxTotalBytes { get; set; } = 64L * 1024 * 1024;
    /// <summary>Un aperçu plus gros n'est pas mis en cache (8 Mo par défaut).</summary>
    public long MaxEntryBytes { get; set; } = 8L * 1024 * 1024;
    /// <summary>Durée de vie absolue d'une entrée (30 minutes par défaut).</summary>
    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>Expiration glissante : entrée oubliée si personne ne la lit (10 minutes).</summary>
    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromMinutes(10);

    public static PreviewPdfCacheOptions FromConfiguration(IConfiguration configuration)
    {
        var o = new PreviewPdfCacheOptions();
        var s = configuration.GetSection("Catalog:PreviewCache");
        if (long.TryParse(s["MaxTotalMegabytes"], out var total) && total > 0) o.MaxTotalBytes = total * 1024 * 1024;
        if (long.TryParse(s["MaxEntryMegabytes"], out var entry) && entry > 0) o.MaxEntryBytes = entry * 1024 * 1024;
        if (int.TryParse(s["AbsoluteMinutes"], out var abs) && abs > 0) o.AbsoluteExpiration = TimeSpan.FromMinutes(abs);
        if (int.TryParse(s["SlidingMinutes"], out var sl) && sl > 0) o.SlidingExpiration = TimeSpan.FromMinutes(sl);
        return o;
    }
}

public sealed class PreviewPdfCache : IPreviewPdfCache, IDisposable
{
    private readonly IDocumentWatermarkService _pdf;
    private readonly PreviewPdfCacheOptions _options;
    private readonly MemoryCache _cache;

    public PreviewPdfCache(IDocumentWatermarkService pdf, PreviewPdfCacheOptions options)
    {
        _pdf = pdf;
        _options = options;
        // Cache dédié : sa limite de taille ne s'impose pas aux autres usages
        // du IMemoryCache partagé (qui n'indiquent pas de taille d'entrée).
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.MaxTotalBytes });
    }

    public async Task<PreviewPdf> GetOrCreateAsync(PreviewPdfKey key, Func<CancellationToken, Task<byte[]>> loadSource, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key.CacheKey, out PreviewPdf? cached) && cached != null)
            return cached;

        var source = await loadSource(ct);
        var total = PdfPageCounter.Count(source);
        var pages = CatalogPreview.ServedPages(key.AllowedPages, total);
        if (pages <= 0)
            throw new PdfPreviewUnavailableException("Document trop court pour un aperçu partiel.");

        // Garde-fou qpdf inclus : lève si la page suivant l'aperçu n'existe pas.
        var cut = await _pdf.ExtractFirstPagesAsync(new MemoryStream(source), pages, ct);
        var preview = new PreviewPdf(cut, pages, total);

        if (cut.LongLength <= _options.MaxEntryBytes)
        {
            _cache.Set(key.CacheKey, preview, new MemoryCacheEntryOptions
            {
                Size = Math.Max(1, cut.LongLength),
                AbsoluteExpirationRelativeToNow = _options.AbsoluteExpiration,
                SlidingExpiration = _options.SlidingExpiration,
            });
        }
        return preview;
    }

    /// <summary>Nombre d'entrées (tests et diagnostic).</summary>
    public int Count => _cache.Count;

    public void Dispose() => _cache.Dispose();
}
