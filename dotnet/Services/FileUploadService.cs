using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;

namespace Backend.Services;

public interface IFileUploadService
{
    Task<string> UploadAvatarAsync(int userId, IFormFile file);
    Task<bool> DeleteAvatarAsync(string avatarUrl);
    bool IsValidImageFile(IFormFile file);
}

public class FileUploadService : IFileUploadService
{
    private readonly ILogger<FileUploadService> _logger;
    private readonly IConfiguration _configuration;
    private const string BucketName = "winplus-bucket";
    private const string KeyPrefix = "avatars/";
    private readonly long _maxFileSize = 5 * 1024 * 1024;
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    public FileUploadService(ILogger<FileUploadService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public bool IsValidImageFile(IFormFile file)
    {
        if (file == null || file.Length == 0) return false;
        if (file.Length > _maxFileSize) return false;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(ext)) return false;

        var allowed = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowed.Contains(file.ContentType?.ToLower() ?? "")) return false;

        return true;
    }

    public async Task<string> UploadAvatarAsync(int userId, IFormFile file)
    {
        if (!IsValidImageFile(file))
            throw new ArgumentException("Invalid image file");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"{KeyPrefix}user_{userId}_{Guid.NewGuid()}{ext}";
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
        _logger.LogInformation("Avatar uploaded to S3 for user {UserId}: {Url}", userId, url);
        return url;
    }

    public async Task<bool> DeleteAvatarAsync(string avatarUrl)
    {
        if (string.IsNullOrEmpty(avatarUrl)) return false;

        try
        {
            if (!avatarUrl.Contains("amazonaws.com"))
            {
                // Legacy local file
                var fileName = Path.GetFileName(avatarUrl);
                var localPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars", fileName);
                if (File.Exists(localPath)) File.Delete(localPath);
                return true;
            }

            var uri = new Uri(avatarUrl);
            var s3Key = uri.AbsolutePath.TrimStart('/');
            var region = _configuration["AWS:Region"] ?? "us-east-1";

            using var s3 = new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region));
            await s3.DeleteObjectAsync(BucketName, s3Key);
            _logger.LogInformation("Avatar deleted from S3: {Key}", s3Key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting avatar {AvatarUrl}", avatarUrl);
            return false;
        }
    }
}
