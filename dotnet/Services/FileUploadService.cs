using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;

namespace Backend.Services;

public interface IFileUploadService
{
    // ── Avatar (existant, signatures inchangées) ──
    Task<string> UploadAvatarAsync(int userId, IFormFile file);
    Task<bool> DeleteAvatarAsync(string avatarUrl);
    bool IsValidImageFile(IFormFile file);

    // ── Couverture de profil (nouveau) ──
    Task<string> UploadCoverAsync(int userId, IFormFile file);
    bool IsValidCoverFile(IFormFile file);

    /// <summary>Suppression générique (avatar ou couverture).</summary>
    Task<bool> DeleteImageAsync(string imageUrl);
}

public class FileUploadService : IFileUploadService
{
    private readonly ILogger<FileUploadService> _logger;
    private readonly IConfiguration _configuration;
    private const string BucketName = "winplus-bucket";
    private const string AvatarPrefix = "avatars/";
    private const string CoverPrefix = "covers/";
    private readonly long _maxAvatarSize = 5 * 1024 * 1024;   // 5 Mo
    private readonly long _maxCoverSize = 8 * 1024 * 1024;    // 8 Mo
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private readonly string[] _allowedContentTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

    public FileUploadService(ILogger<FileUploadService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    // ── Validation ─────────────────────────────────────────────────

    public bool IsValidImageFile(IFormFile file) => IsValid(file, _maxAvatarSize);

    public bool IsValidCoverFile(IFormFile file) => IsValid(file, _maxCoverSize);

    private bool IsValid(IFormFile file, long maxSize)
    {
        if (file == null || file.Length == 0) return false;
        if (file.Length > maxSize) return false;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(ext)) return false;

        if (!_allowedContentTypes.Contains(file.ContentType?.ToLower() ?? "")) return false;

        return true;
    }

    // ── Upload ─────────────────────────────────────────────────────

    public Task<string> UploadAvatarAsync(int userId, IFormFile file)
    {
        if (!IsValidImageFile(file))
            throw new ArgumentException("Invalid image file");

        return UploadAsync(userId, file, AvatarPrefix, "Avatar");
    }

    public Task<string> UploadCoverAsync(int userId, IFormFile file)
    {
        if (!IsValidCoverFile(file))
            throw new ArgumentException("Invalid image file");

        return UploadAsync(userId, file, CoverPrefix, "Cover");
    }

    private async Task<string> UploadAsync(int userId, IFormFile file, string prefix, string label)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"{prefix}user_{userId}_{Guid.NewGuid()}{ext}";
        var region = _configuration["AWS:Region"] ?? "us-east-1";

        var regionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
        using var s3 = new AmazonS3Client(regionEndpoint);

        using var stream = file.OpenReadStream();
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            InputStream = stream,
            ContentType = file.ContentType,
            CannedACL = S3CannedACL.PublicRead
        });

        var url = $"https://{BucketName}.s3.{region}.amazonaws.com/{key}";
        _logger.LogInformation("{Label} uploaded to S3 for user {UserId}: {Url}", label, userId, url);
        return url;
    }

    // ── Suppression ────────────────────────────────────────────────

    public Task<bool> DeleteAvatarAsync(string avatarUrl) => DeleteImageAsync(avatarUrl);

    public async Task<bool> DeleteImageAsync(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return false;

        try
        {
            if (!imageUrl.Contains("amazonaws.com"))
            {
                // Fichier local hérité (wwwroot/uploads/avatars ou /covers)
                var fileName = Path.GetFileName(imageUrl);
                var folder = imageUrl.Contains("/covers/") ? "covers" : "avatars";
                var localPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder, fileName);
                if (File.Exists(localPath)) File.Delete(localPath);
                return true;
            }

            var uri = new Uri(imageUrl);
            var s3Key = uri.AbsolutePath.TrimStart('/');
            var region = _configuration["AWS:Region"] ?? "us-east-1";

            using var s3 = new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region));
            await s3.DeleteObjectAsync(BucketName, s3Key);
            _logger.LogInformation("Image deleted from S3: {Key}", s3Key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting image {ImageUrl}", imageUrl);
            return false;
        }
    }
}
