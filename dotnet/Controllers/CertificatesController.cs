using Backend.Extensions;
using Backend.Models;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/certificates")]
public class CertificatesController : ControllerBase
{
    private readonly ICertificateService _certificateService;
    private readonly ILogger<CertificatesController> _logger;

    public CertificatesController(ICertificateService certificateService, ILogger<CertificatesController> logger)
    {
        _certificateService = certificateService;
        _logger = logger;
    }

    /// <summary>
    /// Generate certificate for completed enrollment
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ApiResponse<CertificateDto>>> GenerateCertificate(GenerateCertificateRequest request)
    {
        try
        {
            var userId = GetUserIdFromToken();
            var certificate = await _certificateService.GenerateCertificateAsync(userId, request.EnrollmentId);

            return CreatedAtAction(nameof(GetCertificate), new { id = certificate.Id }, 
                new ApiResponse<CertificateDto>
                {
                    Success = true,
                    Data = certificate,
                    Timestamp = DateTime.UtcNow
                });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Message = "Enrollment not found",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating certificate");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while generating the certificate",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Get specific certificate by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<CertificateDto>>> GetCertificate(int id)
    {
        try
        {
            var userId = GetUserIdFromToken();
            var certificate = await _certificateService.GetCertificateAsync(id, userId);

            return Ok(new ApiResponse<CertificateDto>
            {
                Success = true,
                Data = certificate,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Message = "Certificate not found",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certificate {CertificateId}", id);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while retrieving the certificate",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Get all certificates for current user
    /// </summary>
    [HttpGet("user/my-certificates")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<CertificateDto>>>> GetUserCertificates()
    {
        try
        {
            var userId = GetUserIdFromToken();
            var certificates = await _certificateService.GetUserCertificatesAsync(userId);

            return Ok(new ApiResponse<List<CertificateDto>>
            {
                Success = true,
                Data = certificates,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user certificates");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while retrieving certificates",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Verify certificate by verification code (public endpoint)
    /// </summary>
    [HttpGet("verify/{verificationCode}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<CertificateVerificationDto>>> VerifyCertificate(string verificationCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(verificationCode))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Verification code is required",
                    Timestamp = DateTime.UtcNow
                });
            }

            var verification = await _certificateService.VerifyCertificateAsync(verificationCode);

            return Ok(new ApiResponse<CertificateVerificationDto>
            {
                Success = verification.IsValid,
                Data = verification,
                Message = verification.IsValid ? "Certificate is valid" : "Certificate not found or invalid",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying certificate");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while verifying the certificate",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Get all certificates for a subject (admin endpoint)
    /// </summary>
    [HttpGet("subject/{subjectId}")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<List<CertificateDto>>>> GetSubjectCertificates(int subjectId)
    {
        try
        {
            var certificates = await _certificateService.GetSubjectCertificatesAsync(subjectId);

            return Ok(new ApiResponse<List<CertificateDto>>
            {
                Success = true,
                Data = certificates,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subject certificates");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while retrieving subject certificates",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Get all certificates (admin only), optional date filters
    /// </summary>
    [HttpGet("admin/all")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<List<CertificateDto>>>> GetAllCertificates(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        try
        {
            var certs = await _certificateService.GetAllCertificatesAsync(from, to);
            return Ok(new ApiResponse<List<CertificateDto>>
            {
                Success = true,
                Data = certs,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all certificates");
            return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Internal server error", Timestamp = DateTime.UtcNow });
        }
    }

    /// <summary>
    /// Admin issues a certificate directly (admin only)
    /// </summary>
    [HttpPost("admin/issue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<CertificateDto>>> AdminIssueCertificate([FromBody] AdminIssueCertificateRequest request)
    {
        try
        {
            var cert = await _certificateService.AdminIssueCertificateAsync(request);
            return CreatedAtAction(nameof(GetCertificate), new { id = cert.Id },
                new ApiResponse<CertificateDto> { Success = true, Data = cert, Timestamp = DateTime.UtcNow });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<object> { Success = false, Message = ex.Message, Timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in admin certificate issuance");
            return StatusCode(500, new ApiResponse<object> { Success = false, Message = "Internal server error", Timestamp = DateTime.UtcNow });
        }
    }

    private int GetUserIdFromToken()
    {
        var userIdClaim = User.FindFirst("sub") ?? User.FindFirst("userId");
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }
        return userId;
    }
}
