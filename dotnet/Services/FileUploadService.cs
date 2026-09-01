using Microsoft.AspNetCore.Http;

namespace Backend.Services;

/// <summary>
/// CORRECTIF : le bucket « winplus-bucket » n'est plus codé en dur ici et le
/// client S3 n'est plus créé dans ce service. Tout passe par IStorageService,
/// qui vérifie l'existence du bucket, retire l'ACL publique (rejetée par les
/// buckets « Bucket owner enforced ») et bascule sur wwwroot/uploads si S3 est
/// indisponible. Les signatures publiques de l'interface sont inchangées : aucun
/// appelant existant n'est à modifier.
/// </summary>
public interface IFileUploadService
{
    // ── Avatar (existant, signatures inchangées) ──
    Task<string> UploadAvatarAsync(int userId, IFormFile file);
    Task<bool> DeleteAvatarAsync(string avatarUrl);
    bool IsValidImageFile(IFormFile file);

    // ── Couverture de profil ──
    Task<string> UploadCoverAsync(int userId, IFormFile file);
    bool IsValidCoverFile(IFormFile file);

    /// <summary>Suppression générique (avatar ou couverture).</summary>
    Task<bool> DeleteImageAsync(string imageUrl);
}

public class FileUploadService : IFileUploadService
{
    private readonly ILogger<FileUploadService> _logger;
    private readonly IStorageService _storage;

    private const string AvatarPrefix = "avatars/";
    private const string CoverPrefix = "covers/";
    private readonly long _maxAvatarSize = 5 * 1024 * 1024;   // 5 Mo
    private readonly long _maxCoverSize = 8 * 1024 * 1024;    // 8 Mo
    private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private readonly string[] _allowedContentTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

    public FileUploadService(ILogger<FileUploadService> logger, IStorageService storage)
    {
        _logger = logger;
        _storage = storage;
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

        // Certains navigateurs envoient image/jpg ou un type vide : on ne rejette
        // que si le type est renseigné ET inconnu, l'extension faisant foi.
        var ct = file.ContentType?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(ct) && !_allowedContentTypes.Contains(ct) && ct != "image/jpg")
            return false;

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
        var key = $"{prefix}user_{userId}_{Guid.NewGuid():N}{ext}";

        await using var stream = file.OpenReadStream();
        var url = await _storage.PutAsync(stream, key, file.ContentType);

        _logger.LogInformation("{Label} uploaded for user {UserId}: {Url}", label, userId, url);
        return url;
    }

    // ── Suppression ────────────────────────────────────────────────

    public Task<bool> DeleteAvatarAsync(string avatarUrl) => DeleteImageAsync(avatarUrl);

    public Task<bool> DeleteImageAsync(string imageUrl) => _storage.DeleteAsync(imageUrl);
}
