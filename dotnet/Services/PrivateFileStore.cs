using Amazon.S3;
using Amazon.S3.Model;

namespace Backend.Services;

/// <summary>
/// Stockage PRIVÉ (lot 9.5, suivi 17.16) : pièces d'identité et selfies des
/// tuteurs. Contrairement à <see cref="IStorageService.PutAsync"/> (qui renvoie
/// une URL publique et peut retomber sur wwwroot/uploads, servi publiquement),
/// ce service :
/// <list type="bullet">
///   <item>n'envoie jamais d'ACL publique et ne renvoie jamais d'URL : seulement
///   une référence opaque « s3:clé » ou « local:clé » gardée en base ;</item>
///   <item>range les objets S3 sous le préfixe <c>private/</c> du bucket
///   <c>Storage:PrivateBucket</c> (par défaut le bucket principal) : la bucket
///   policy de lecture publique ne doit PAS couvrir ce préfixe (prérequis de
///   déploiement) ;</item>
///   <item>sans S3, écrit dans <c>Storage:PrivateLocalRoot</c> (par défaut
///   « private_uploads » à la racine de l'application, hors de wwwroot, donc
///   jamais servi par les fichiers statiques), sauf si
///   <c>Storage:PrivateRequireS3</c> = true ;</item>
///   <item>ne donne accès qu'aux appelants qui l'utilisent (écrans admin) : URL
///   S3 présignée courte, ou lecture du fichier local par un endpoint admin.</item>
/// </list>
/// </summary>
public interface IPrivateFileStore
{
    /// <summary>Enregistre le contenu et renvoie une référence opaque (jamais une URL).</summary>
    Task<string> PutAsync(Stream content, string key, string? contentType, CancellationToken ct = default);

    /// <summary>URL S3 présignée de durée <paramref name="ttl"/> ; null pour un fichier local (lu via <see cref="OpenLocalAsync"/>).</summary>
    string? GetTemporaryUrl(string reference, TimeSpan ttl);

    /// <summary>Flux d'un fichier privé local ; null s'il n'existe pas ou n'est pas local.</summary>
    Task<(Stream Content, string ContentType)?> OpenLocalAsync(string reference, CancellationToken ct = default);

    /// <summary>Supprime le fichier ; true si supprimé ou déjà absent.</summary>
    Task<bool> DeleteAsync(string reference, CancellationToken ct = default);
}

public class PrivateFileStore : IPrivateFileStore
{
    public const string S3Prefix = "s3:";
    public const string LocalPrefix = "local:";

    private readonly IStorageService _storage;
    private readonly IConfiguration _config;
    private readonly ILogger<PrivateFileStore> _logger;

    public PrivateFileStore(IStorageService storage, IConfiguration config, ILogger<PrivateFileStore> logger)
    {
        _storage = storage;
        _config = config;
        _logger = logger;
    }

    private string Bucket => string.IsNullOrWhiteSpace(_config["Storage:PrivateBucket"]) ? _storage.Bucket : _config["Storage:PrivateBucket"]!;
    private bool RequireS3 => _config.GetValue("Storage:PrivateRequireS3", false);
    private string LocalRoot => _config["Storage:PrivateLocalRoot"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "private_uploads");

    /// <summary>Clé normalisée sous « private/ », sans « .. » ni caractères exotiques.</summary>
    public static string NormalizeKey(string key)
    {
        var segments = key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(seg => string.Concat(seg.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
            .Where(seg => seg.Length > 0 && seg != "." && seg != "..")
            .ToList();
        if (segments.Count == 0 || segments[0] != "private") segments.Insert(0, "private");
        return string.Join('/', segments);
    }

    public async Task<string> PutAsync(Stream content, string key, string? contentType, CancellationToken ct = default)
    {
        key = NormalizeKey(key);
        if (await _storage.IsS3ReadyAsync(ct))
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            using var s3 = _storage.CreateS3Client();
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = Bucket,
                Key = key,
                InputStream = buffer,
                AutoCloseStream = false,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                // Jamais d'ACL publique ; chiffrement côté serveur S3.
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
            }, ct);
            return S3Prefix + key;
        }

        if (RequireS3)
            throw new InvalidOperationException("Stockage privé indisponible pour l'instant. Réessaie dans quelques minutes.");

        var full = LocalPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None))
            await content.CopyToAsync(fs, ct);
        _logger.LogInformation("Stockage privé local : {Key}", key);
        return LocalPrefix + key;
    }

    public string? GetTemporaryUrl(string reference, TimeSpan ttl)
    {
        if (!reference.StartsWith(S3Prefix, StringComparison.Ordinal)) return null;
        using var s3 = _storage.CreateS3Client();
        return s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = reference[S3Prefix.Length..],
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl),
        });
    }

    public Task<(Stream Content, string ContentType)?> OpenLocalAsync(string reference, CancellationToken ct = default)
    {
        if (!reference.StartsWith(LocalPrefix, StringComparison.Ordinal))
            return Task.FromResult<(Stream, string)?>(null);
        var full = LocalPath(reference[LocalPrefix.Length..]);
        if (!File.Exists(full))
            return Task.FromResult<(Stream, string)?>(null);
        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<(Stream, string)?>((stream, ContentTypeOf(full)));
    }

    public async Task<bool> DeleteAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            if (reference.StartsWith(S3Prefix, StringComparison.Ordinal))
            {
                if (StorageService.PreserveRemoteObjects(_config)) return true;
                using var s3 = _storage.CreateS3Client();
                await s3.DeleteObjectAsync(Bucket, reference[S3Prefix.Length..], ct);
                return true;
            }
            if (reference.StartsWith(LocalPrefix, StringComparison.Ordinal))
            {
                var full = LocalPath(reference[LocalPrefix.Length..]);
                if (File.Exists(full)) File.Delete(full);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stockage privé : suppression impossible ({Reference})", reference);
            return false;
        }
    }

    private string LocalPath(string key)
    {
        var root = Path.GetFullPath(LocalRoot);
        var full = Path.GetFullPath(Path.Combine(root, NormalizeKey(key).Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chemin de fichier privé invalide.");
        return full;
    }

    public static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}
