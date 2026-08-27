using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Extensions;

namespace Backend.Controllers;

/// <summary>
/// Image de couverture du profil + lecture groupée des médias.
/// Volontairement séparé de UsersController pour ne pas toucher à ce
/// fichier : les routes se complètent, elles n'entrent pas en conflit.
///
/// GET    /api/users/profile/media
/// POST   /api/users/profile/cover   (multipart, champ « file »)
/// DELETE /api/users/profile/cover
/// </summary>
[ApiController]
[Route("api/users/profile")]
[Authorize]
public class ProfileMediaController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IFileUploadService _fileUploadService;
    private readonly ILogger<ProfileMediaController> _logger;

    public ProfileMediaController(
        IUserService userService,
        IFileUploadService fileUploadService,
        ILogger<ProfileMediaController> logger)
    {
        _userService = userService;
        _fileUploadService = fileUploadService;
        _logger = logger;
    }

    [HttpGet("media")]
    public async Task<IActionResult> GetMedia()
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(User.GetUserId());
            if (user == null) return NotFound();

            return Ok(new { avatarUrl = user.AvatarUrl, coverUrl = user.CoverUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile media");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpPost("cover")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadCover([FromForm] IFormFile file)
    {
        try
        {
            var userId = User.GetUserId();

            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            if (!_fileUploadService.IsValidCoverFile(file))
                return BadRequest(new { error = "Invalid image file. Allowed: JPG, PNG, GIF, WEBP. Max size: 8MB" });

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            if (!string.IsNullOrEmpty(user.CoverUrl))
                await _fileUploadService.DeleteImageAsync(user.CoverUrl);

            var coverUrl = await _fileUploadService.UploadCoverAsync(userId, file);
            user.CoverUrl = coverUrl;
            await _userService.UpdateUserAsync(user);

            return Ok(new { coverUrl });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading cover");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpDelete("cover")]
    public async Task<IActionResult> DeleteCover()
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(User.GetUserId());

            if (user == null || string.IsNullOrEmpty(user.CoverUrl))
                return NotFound(new { error = "No cover to delete" });

            await _fileUploadService.DeleteImageAsync(user.CoverUrl);
            user.CoverUrl = null;
            await _userService.UpdateUserAsync(user);

            return Ok(new { message = "Cover deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting cover");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
