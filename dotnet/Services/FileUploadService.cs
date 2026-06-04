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
    private readonly string _uploadPath;
    private readonly long _maxFileSize = 5 * 1024 * 1024; // 5 MB
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    public FileUploadService(ILogger<FileUploadService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Path for local storage (can be replaced by S3)
        _uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
        
        // Create folder if it doesn't exist
        if (!Directory.Exists(_uploadPath))
        {
            Directory.CreateDirectory(_uploadPath);
        }
    }

    public bool IsValidImageFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return false;

        // Check size
        if (file.Length > _maxFileSize)
            return false;

        // Check extension
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_allowedExtensions.Contains(extension))
            return false;

        // Check MIME type
        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedMimeTypes.Contains(file.ContentType?.ToLower() ?? ""))
            return false;

        return true;
    }

    public async Task<string> UploadAvatarAsync(int userId, IFormFile file)
    {
        try
        {
            if (!IsValidImageFile(file))
            {
                throw new ArgumentException("Invalid image file");
            }

            // Generate unique filename
            var fileName = $"user_{userId}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(_uploadPath, fileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Relative URL
            var avatarUrl = $"/uploads/avatars/{fileName}";

            _logger.LogInformation("Avatar uploaded for user {UserId}: {AvatarUrl}", userId, avatarUrl);

            return avatarUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading avatar for user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> DeleteAvatarAsync(string avatarUrl)
    {
        try
        {
            if (string.IsNullOrEmpty(avatarUrl))
                return false;

            // Extract filename
            var fileName = Path.GetFileName(avatarUrl);
            var filePath = Path.Combine(_uploadPath, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Avatar deleted: {AvatarUrl}", avatarUrl);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting avatar {AvatarUrl}", avatarUrl);
            return false;
        }
    }
}
