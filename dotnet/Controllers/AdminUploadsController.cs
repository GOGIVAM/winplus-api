using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Data;

namespace Backend.Controllers;

/// <summary>
/// Téléversement de gros fichiers (épreuves PDF, vidéos de plusieurs Go).
///
/// Le fichier ne traverse PAS l'API : le navigateur découpe le fichier et
/// envoie chaque partie directement à S3 via une URL signée. On évite ainsi
/// les trois murs qui faisaient échouer les envois volumineux — la limite de
/// corps de requête de nginx, celle de Kestrel, et le délai d'attente du
/// proxy — et une coupure réseau ne fait perdre qu'une partie, pas le fichier.
///
/// Cycle de vie :
///   1. POST   /api/admin/uploads/init        → key + uploadId + taille de partie
///   2. POST   /api/admin/uploads/part-url    → une URL signée par partie (PUT)
///   3. POST   /api/admin/uploads/complete    → assemblage S3, renvoie l'URL publique
///      POST   /api/admin/uploads/abort       → abandon, S3 purge les fragments
///      GET    /api/admin/uploads/parts       → parties déjà reçues (reprise)
///
/// ⚠️ Le bucket doit autoriser le PUT depuis le domaine du site (CORS) et les
/// en-têtes ETag en lecture — voir README-PATCH.md.
/// </summary>
[ApiController]
[Route("api/admin/uploads")]
[Authorize(Policy = "AdminOnly")]
public class AdminUploadsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminUploadsController> _logger;

    /// 8 Mio : au-dessus de la limite S3 (5 Mio minimum par partie), assez petit
    /// pour qu'une reprise sur connexion lente ne coûte pas cher.
    private const int PartSize = 8 * 1024 * 1024;

    /// 5 Tio est la limite S3 ; on plafonne à 5 Go, au-delà c'est une erreur de
    /// manipulation plus probable qu'un cours.
    private const long MaxTotalBytes = 5L * 1024 * 1024 * 1024;

    private static readonly string[] AllowedExtensions =
    {
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".zip",
        ".jpg", ".jpeg", ".png", ".webp",
        ".mp4", ".webm", ".mov", ".m4v", ".mp3", ".m4a",
    };

    public AdminUploadsController(IConfiguration configuration, ILogger<AdminUploadsController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string Bucket => _configuration["AWS:Bucket"] ?? "winplus-bucket";
    private string Region => _configuration["AWS:Region"] ?? "us-east-1";
    private IAmazonS3 Client() => new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(Region));
    private string PublicUrl(string key) => $"https://{Bucket}.s3.{Region}.amazonaws.com/{key}";

    public record InitRequest(string FileName, string? ContentType, long? TotalBytes, string? Folder);
    public record PartUrlRequest(string Key, string UploadId, int PartNumber);
    public record CompletedPart(int PartNumber, string ETag);
    public record CompleteRequest(string Key, string UploadId, List<CompletedPart> Parts);
    public record AbortRequest(string Key, string UploadId);

    /// <summary>Ouvre un envoi en plusieurs parties.</summary>
    [HttpPost("init")]
    public async Task<IActionResult> Init([FromBody] InitRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.FileName))
            return BadRequest(new { error = "Nom de fichier requis" });

        var ext = Path.GetExtension(body.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = $"Extension non autorisée ({ext})" });

        if (body.TotalBytes is > MaxTotalBytes)
            return BadRequest(new { error = "Fichier trop volumineux (5 Go maximum)" });

        var folder = string.IsNullOrWhiteSpace(body.Folder) ? "exams" : Sanitize(body.Folder!);
        var key = $"{folder}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{ext}";

        try
        {
            using var s3 = Client();
            var res = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
            {
                BucketName  = Bucket,
                Key         = key,
                ContentType = string.IsNullOrWhiteSpace(body.ContentType) ? "application/octet-stream" : body.ContentType,
                CannedACL   = S3CannedACL.PublicRead,
            });

            return Ok(new
            {
                success = true,
                data = new { key, uploadId = res.UploadId, partSize = PartSize, maxTotalBytes = MaxTotalBytes },
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Init multipart upload failed for {FileName}", body.FileName);
            return StatusCode(500, new { error = "Impossible d'ouvrir le téléversement" });
        }
    }

    /// <summary>URL signée pour envoyer une partie (PUT direct vers S3, 2 h de validité).</summary>
    [HttpPost("part-url")]
    public IActionResult PartUrl([FromBody] PartUrlRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Key) || string.IsNullOrWhiteSpace(body.UploadId))
            return BadRequest(new { error = "Key et uploadId requis" });
        if (body.PartNumber < 1 || body.PartNumber > 10000)
            return BadRequest(new { error = "Numéro de partie hors limites" });

        try
        {
            using var s3 = Client();
            var url = s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = Bucket,
                Key        = body.Key,
                UploadId   = body.UploadId,
                PartNumber = body.PartNumber,
                Verb       = HttpVerb.PUT,
                Expires    = DateTime.UtcNow.AddHours(2),
            });

            return Ok(new { success = true, data = new { url, partNumber = body.PartNumber } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Presign part {Part} failed for {Key}", body.PartNumber, body.Key);
            return StatusCode(500, new { error = "Impossible de signer cette partie" });
        }
    }

    /// <summary>Parties déjà reçues par S3 — sert à reprendre un envoi interrompu.</summary>
    [HttpGet("parts")]
    public async Task<IActionResult> Parts([FromQuery] string key, [FromQuery] string uploadId)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(uploadId))
            return BadRequest(new { error = "key et uploadId requis" });

        try
        {
            using var s3 = Client();
            var res = await s3.ListPartsAsync(new ListPartsRequest
            {
                BucketName = Bucket, Key = key, UploadId = uploadId,
            });

            return Ok(new
            {
                success = true,
                data = res.Parts.Select(p => new { partNumber = p.PartNumber, eTag = p.ETag, size = p.Size }),
            });
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload")
        {
            return NotFound(new { error = "Téléversement inconnu ou déjà finalisé" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListParts failed for {Key}", key);
            return StatusCode(500, new { error = "Impossible de lire l'état du téléversement" });
        }
    }

    /// <summary>Assemble les parties et renvoie l'URL publique du fichier.</summary>
    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteRequest body)
    {
        if (body.Parts == null || body.Parts.Count == 0)
            return BadRequest(new { error = "Aucune partie déclarée" });

        try
        {
            using var s3 = Client();
            await s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                BucketName = Bucket,
                Key        = body.Key,
                UploadId   = body.UploadId,
                PartETags  = body.Parts
                    .OrderBy(p => p.PartNumber)
                    .Select(p => new PartETag(p.PartNumber, p.ETag))
                    .ToList(),
            });

            var url = PublicUrl(body.Key);
            _logger.LogInformation("Upload completed: {Url} ({Parts} parties)", url, body.Parts.Count);
            return Ok(new { success = true, data = new { url, key = body.Key } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Complete multipart failed for {Key}", body.Key);
            return StatusCode(500, new { error = "Assemblage du fichier impossible" });
        }
    }

    /// <summary>Abandonne l'envoi : S3 supprime les fragments déjà reçus.</summary>
    [HttpPost("abort")]
    public async Task<IActionResult> Abort([FromBody] AbortRequest body)
    {
        try
        {
            using var s3 = Client();
            await s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
            {
                BucketName = Bucket, Key = body.Key, UploadId = body.UploadId,
            });
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Abort multipart failed for {Key}", body.Key);
            return Ok(new { success = false });
        }
    }

    /// <summary>
    /// Envoi direct, pour les petits fichiers (≤ 25 Mo) : une seule requête, pas
    /// de découpage. Utilisé pour les images de vignette.
    /// </summary>
    [HttpPost("direct")]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<IActionResult> Direct(IFormFile file, [FromQuery] string? folder = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Fichier requis" });
        if (file.Length > 25 * 1024 * 1024)
            return BadRequest(new { error = "Au-delà de 25 Mo, utilisez l'envoi en plusieurs parties" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = $"Extension non autorisée ({ext})" });

        var dir = string.IsNullOrWhiteSpace(folder) ? "exams" : Sanitize(folder!);
        var key = $"{dir}/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{ext}";

        try
        {
            using var s3 = Client();
            using var stream = file.OpenReadStream();
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = Bucket,
                Key         = key,
                InputStream = stream,
                ContentType = file.ContentType,
                CannedACL   = S3CannedACL.PublicRead,
            });

            return Ok(new { success = true, data = new { url = PublicUrl(key), key } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct upload failed for {FileName}", file.FileName);
            return StatusCode(500, new { error = "Téléversement impossible" });
        }
    }

    private static string Sanitize(string s) =>
        new string(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '/').ToArray())
            .Trim('/')
            .ToLowerInvariant() is { Length: > 0 } clean ? clean : "exams";
}
