using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

namespace Backend.Services;

/// <summary>
/// Point unique de stockage des fichiers (S3 ou disque local).
///
/// Pourquoi ce service existe : le nom du bucket était codé en dur dans quatre
/// endroits (AdminUploadsController, FileUploadService, AdminController,
/// CertificateService) avec deux clés de configuration différentes
/// (AWS:Bucket et AWS:BucketName). Le bucket « winplus-bucket » n'existe pas sur
/// le compte AWS utilisé → tout envoi remonte
/// « Amazon.S3.AmazonS3Exception: The specified bucket does not exist ».
///
/// Ce service :
///   1. lit le bucket/la région dans la configuration (une seule source),
///   2. vérifie une fois pour toutes que le bucket existe (et peut le créer si
///      Storage:AutoCreateBucket = true),
///   3. n'envoie plus d'ACL publique par défaut : les buckets créés depuis avril
///      2023 ont « Object Ownership = Bucket owner enforced », qui rejette
///      CannedACL.PublicRead avec « AccessControlListNotSupported ». La lecture
///      publique se règle par une bucket policy (voir PATCH.md),
///   4. bascule sur wwwroot/uploads si S3 est indisponible ou non configuré, au
///      lieu de renvoyer une 500 à l'utilisateur.
/// </summary>
public interface IStorageService
{
    string Bucket { get; }
    string Region { get; }

    /// <summary>true si les envois partent réellement vers S3.</summary>
    Task<bool> IsS3ReadyAsync(CancellationToken ct = default);

    /// <summary>Client S3 configuré (pour le multipart / les URL signées).</summary>
    IAmazonS3 CreateS3Client();

    /// <summary>Envoie le contenu et renvoie l'URL publique.</summary>
    Task<string> PutAsync(Stream content, string key, string? contentType, CancellationToken ct = default);

    Task<bool> DeleteAsync(string urlOrKey, CancellationToken ct = default);

    /// <summary>URL publique d'une clé S3.</summary>
    string PublicUrl(string key);
}

public class StorageService : IStorageService
{
    private readonly IConfiguration _config;
    private readonly ILogger<StorageService> _logger;
    private readonly SemaphoreSlim _probeLock = new(1, 1);

    private bool? _s3Ready;
    private bool _usePublicAcl;

    public StorageService(IConfiguration config, ILogger<StorageService> logger)
    {
        _config = config;
        _logger = logger;
        _usePublicAcl = _config.GetValue("Storage:UsePublicAcl", false);
    }

    // ── Configuration ──────────────────────────────────────────────────────
    // Les anciennes clés (AWS:Bucket, AWS:BucketName) restent lues pour ne rien
    // casser sur les déploiements existants.

    public string Bucket =>
        _config["Storage:Bucket"]
        ?? _config["AWS:Bucket"]
        ?? _config["AWS:BucketName"]
        ?? Environment.GetEnvironmentVariable("AWS_BUCKET")
        ?? "";

    public string Region =>
        _config["Storage:Region"]
        ?? _config["AWS:Region"]
        ?? Environment.GetEnvironmentVariable("AWS_REGION")
        ?? "us-east-1";

    private string Provider => (_config["Storage:Provider"] ?? "auto").ToLowerInvariant();
    private bool AutoCreateBucket => _config.GetValue("Storage:AutoCreateBucket", false);
    private bool FallbackToLocal => _config.GetValue("Storage:FallbackToLocal", true);
    private string LocalRoot => _config["Storage:LocalRoot"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
    private string? PublicBaseUrl => _config["Storage:PublicBaseUrl"]?.TrimEnd('/');

    public IAmazonS3 CreateS3Client()
    {
        var endpoint = Amazon.RegionEndpoint.GetBySystemName(Region);
        var serviceUrl = _config["Storage:ServiceUrl"]; // S3 compatible (MinIO, Scaleway…)
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            return new AmazonS3Client(new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = Region,
            });
        }
        return new AmazonS3Client(endpoint);
    }

    public string PublicUrl(string key)
    {
        if (!string.IsNullOrWhiteSpace(PublicBaseUrl))
            return $"{PublicBaseUrl}/{key.TrimStart('/')}";
        return $"https://{Bucket}.s3.{Region}.amazonaws.com/{key.TrimStart('/')}";
    }

    // ── Vérification unique du bucket ──────────────────────────────────────

    public async Task<bool> IsS3ReadyAsync(CancellationToken ct = default)
    {
        if (Provider == "local") return false;
        if (_s3Ready.HasValue) return _s3Ready.Value;

        await _probeLock.WaitAsync(ct);
        try
        {
            if (_s3Ready.HasValue) return _s3Ready.Value;

            if (string.IsNullOrWhiteSpace(Bucket))
            {
                _logger.LogWarning(
                    "Stockage : aucun bucket configuré (Storage:Bucket / AWS:Bucket). " +
                    "Les fichiers seront écrits en local dans {LocalRoot}.", LocalRoot);
                return (_s3Ready = false).Value;
            }

            using var s3 = CreateS3Client();
            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3, Bucket);

            if (!exists && AutoCreateBucket)
            {
                try
                {
                    await s3.PutBucketAsync(new PutBucketRequest
                    {
                        BucketName = Bucket,
                        UseClientRegion = true,
                    }, ct);
                    _logger.LogWarning("Stockage : bucket {Bucket} créé dans {Region}.", Bucket, Region);
                    exists = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Stockage : création du bucket {Bucket} impossible.", Bucket);
                }
            }

            if (!exists)
            {
                _logger.LogError(
                    "Stockage : le bucket « {Bucket} » n'existe pas dans la région {Region} " +
                    "(c'est la cause du « The specified bucket does not exist »). " +
                    "Créez-le, corrigez Storage:Bucket, ou activez Storage:AutoCreateBucket. " +
                    "Repli local actif : {Fallback}.", Bucket, Region, FallbackToLocal);
            }

            return (_s3Ready = exists).Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stockage : S3 injoignable, repli local ({Fallback}).", FallbackToLocal);
            return (_s3Ready = false).Value;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    // ── Envoi ──────────────────────────────────────────────────────────────

    public async Task<string> PutAsync(Stream content, string key, string? contentType, CancellationToken ct = default)
    {
        key = key.TrimStart('/');

        // Bufferisé : la retentative sans ACL doit pouvoir relire le flux, et un
        // IFormFile.OpenReadStream() n'est pas toujours rejouable.
        MemoryStream buffer;
        if (content is MemoryStream ms && ms.CanSeek)
        {
            ms.Position = 0;
            buffer = ms;
        }
        else
        {
            buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
        }

        if (await IsS3ReadyAsync(ct))
        {
            try
            {
                return await PutToS3Async(buffer, key, contentType, ct);
            }
            catch (AmazonS3Exception ex) when (IsAclRefused(ex) && _usePublicAcl)
            {
                // Bucket avec « Object Ownership: Bucket owner enforced ».
                _usePublicAcl = false;
                _logger.LogWarning(
                    "Stockage : ACL publique refusée par {Bucket}, nouvel essai sans ACL " +
                    "(rendez le bucket lisible via une bucket policy).", Bucket);
                return await PutToS3Async(buffer, key, contentType, ct);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                _s3Ready = false;
                _logger.LogError(ex, "Stockage : bucket {Bucket} absent au moment de l'envoi.", Bucket);
                if (!FallbackToLocal) throw;
            }
            catch (Exception ex) when (FallbackToLocal)
            {
                _logger.LogError(ex, "Stockage : envoi S3 échoué pour {Key}, repli local.", key);
            }
        }
        else if (!FallbackToLocal)
        {
            throw new InvalidOperationException(
                $"Stockage indisponible : le bucket « {Bucket} » n'existe pas dans {Region}.");
        }

        return await PutLocalAsync(buffer, key, ct);
    }

    private async Task<string> PutToS3Async(MemoryStream buffer, string key, string? contentType, CancellationToken ct)
    {
        buffer.Position = 0;
        using var s3 = CreateS3Client();
        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = buffer,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            AutoCloseStream = false,
        };
        if (_usePublicAcl) request.CannedACL = S3CannedACL.PublicRead;

        await s3.PutObjectAsync(request, ct);
        return PublicUrl(key);
    }

    private async Task<string> PutLocalAsync(MemoryStream buffer, string key, CancellationToken ct)
    {
        var safeKey = string.Join('/', key.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(seg => string.Concat(seg.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))))
            .Trim('/');
        if (string.IsNullOrWhiteSpace(safeKey))
            safeKey = $"misc/{Guid.NewGuid():N}";

        var full = Path.Combine(LocalRoot, safeKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        buffer.Position = 0;
        await using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None))
            await buffer.CopyToAsync(fs, ct);

        var url = string.IsNullOrWhiteSpace(PublicBaseUrl)
            ? $"/uploads/{safeKey}"
            : $"{PublicBaseUrl}/uploads/{safeKey}";

        _logger.LogInformation("Stockage local : {Url}", url);
        return url;
    }

    // ── Suppression ────────────────────────────────────────────────────────

    public async Task<bool> DeleteAsync(string urlOrKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrKey)) return false;

        try
        {
            var isRemote = urlOrKey.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            var key = isRemote ? new Uri(urlOrKey).AbsolutePath.TrimStart('/') : urlOrKey.TrimStart('/');

            if (isRemote && urlOrKey.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!await IsS3ReadyAsync(ct)) return false;
                using var s3 = CreateS3Client();
                await s3.DeleteObjectAsync(Bucket, key, ct);
                return true;
            }

            var local = key.StartsWith("uploads/") ? key["uploads/".Length..] : key;
            var full = Path.Combine(LocalRoot, local.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) File.Delete(full);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stockage : suppression impossible pour {Target}", urlOrKey);
            return false;
        }
    }

    private static bool IsAclRefused(AmazonS3Exception ex) =>
        ex.ErrorCode == "AccessControlListNotSupported"
        || ex.ErrorCode == "InvalidBucketAclWithObjectOwnership"
        || (ex.Message?.Contains("ACL", StringComparison.OrdinalIgnoreCase) == true
            && ex.StatusCode == System.Net.HttpStatusCode.BadRequest);
}
