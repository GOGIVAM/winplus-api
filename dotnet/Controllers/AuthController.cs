using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Services;
using Backend.Models;
using Backend.Models.DTOs;
using Backend.Data;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ICustomAuthService _customAuthService;
    private readonly ICartService _cartService;
    private readonly IAnonymousCartService _anonymousCartService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ICustomAuthService customAuthService,
        ICartService cartService,
        IAnonymousCartService anonymousCartService,
        ILogger<AuthController> logger)
    {
        _customAuthService = customAuthService;
        _cartService = cartService;
        _anonymousCartService = anonymousCartService;
        _logger = logger;
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SignUpResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequestDto request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required" });
        }

        try
        {
            _logger.LogInformation(
                "[SignUp Attempt] 📝 Tentative d'inscription\n" +
                "Email: {Email}\n" +
                "FirstName: {FirstName}\n" +
                "LastName: {LastName}\n" +
                "IP: {IpAddress}\n" +
                "Timestamp: {Timestamp}",
                request.Email,
                request.FirstName,
                request.LastName,
                HttpContext.Connection.RemoteIpAddress,
                DateTime.UtcNow
            );

            var result = await _customAuthService.SignUpAsync(
                request.Email,
                request.Password,
                request.FirstName ?? "",
                request.LastName ?? "",
                request.Phone);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "[SignUp Failed] ❌ Inscription échouée\n" +
                    "Email: {Email}\n" +
                    "Reason: {Message}\n" +
                    "Errors: {Errors}\n" +
                    "Timestamp: {Timestamp}",
                    request.Email, 
                    result.Message,
                    string.Join(", ", result.Errors?.Select(x => $"{x.Key}: {x.Value}") ?? new List<string>()),
                    DateTime.UtcNow
                );
                return BadRequest(new { error = result.Message, details = result.Errors });
            }

            _logger.LogInformation(
                "[SignUp Success] ✅ Inscription réussie\n" +
                "Email: {Email}\n" +
                "UserId: {UserId}\n" +
                "IsEmailVerified: {IsEmailVerified}\n" +
                "Timestamp: {Timestamp}",
                request.Email,
                result.User?.Id,
                result.User?.IsEmailVerified,
                DateTime.UtcNow
            );

            return Ok(new SignUpResponse
            {
                Message = result.Message,
                User = new
                {
                    email = result.User?.Email,
                    firstName = result.User?.FirstName,
                    lastName = result.User?.LastName,
                    isEmailVerified = result.User?.IsEmailVerified
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SignUp Error] ❌ Erreur durant l'inscription\n" +
                "Email: {Email}\n" +
                "Exception: {ExceptionMessage}\n" +
                "StackTrace: {StackTrace}",
                request.Email,
                ex.Message,
                ex.StackTrace
            );
            return StatusCode(500, new { error = "An error occurred during registration", details = ex.Message });
        }
    }

    /// <summary>
    /// Sign in with email and password
    /// </summary>
    [HttpPost("signin")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SignInResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SignIn([FromBody] SignInRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required" });
        }

        try
        {
            _logger.LogInformation(
                "[SignIn Attempt] 🔓 Tentative de connexion\n" +
                "Email: {Email}\n" +
                "IP: {IpAddress}\n" +
                "UserAgent: {UserAgent}\n" +
                "Timestamp: {Timestamp}",
                request.Email,
                HttpContext.Connection.RemoteIpAddress,
                Request.Headers.UserAgent.ToString(),
                DateTime.UtcNow
            );

            var result = await _customAuthService.SignInAsync(request.Email, request.Password, HttpContext.Request, request.RememberMe);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "[SignIn Failed] ❌ Connexion échouée\n" +
                    "Email: {Email}\n" +
                    "Reason: {Message}\n" +
                    "ErrorCode: {ErrorCode}\n" +
                    "Timestamp: {Timestamp}",
                    request.Email, 
                    result.Message,
                    result.ErrorCode,
                    DateTime.UtcNow
                );
                
                // ✅ AMÉLIORATION : Retourner un code d'erreur spécifique et l'info resend
                var errorResponse = new
                {
                    error = result.Message,
                    errorCode = result.ErrorCode ?? "INVALID_CREDENTIALS",
                    canResendVerification = result.ErrorCode == "EMAIL_NOT_VERIFIED",
                    details = result.Errors
                };
                
                return Unauthorized(errorResponse);
            }

            _logger.LogInformation(
                "[SignIn Success] ✅ Connexion réussie\n" +
                "Email: {Email}\n" +
                "UserId: {UserId}\n" +
                "Role: {Role}\n" +
                "AccessTokenLength: {TokenLength}\n" +
                "RefreshTokenLength: {RefreshTokenLength}\n" +
                "ExpiresIn: {ExpiresIn} secondes\n" +
                "Timestamp: {Timestamp}",
                request.Email,
                result.User?.Id,
                result.User?.Role,
                result.AccessToken?.Length,
                result.RefreshToken?.Length,
                result.ExpiresIn ?? 900,
                DateTime.UtcNow
            );

            // ✅ FUSION DU PANIER: Si l'utilisateur avait un panier anonyme, le fusionner
            if (!string.IsNullOrEmpty(request.DeviceId) && result.User?.Id > 0)
            {
                try
                {
                    var anonymousItems = _anonymousCartService.GetAnonymousCart(request.DeviceId);
                    if (anonymousItems.Any())
                    {
                        await _cartService.MergeAnonymousCartAsync(result.User.Id, anonymousItems);
                        _anonymousCartService.ClearAnonymousCart(request.DeviceId);
                        
                        _logger.LogInformation(
                            "[SignIn CartMerge] ✅ Panier anonyme fusionné avec succès\n" +
                            "UserId: {UserId}\n" +
                            "DeviceId: {DeviceId}\n" +
                            "ItemsMerged: {ItemsCount}\n" +
                            "Timestamp: {Timestamp}",
                            result.User.Id,
                            request.DeviceId,
                            anonymousItems.Count,
                            DateTime.UtcNow
                        );
                    }
                }
                catch (Exception mergeEx)
                {
                    _logger.LogError(mergeEx, "[SignIn CartMerge] ❌ Erreur lors de la fusion du panier");
                    // Ne pas bloquer le SignIn si la fusion échoue
                }
            }

            return Ok(new SignInResponse
            {
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                ExpiresIn = result.ExpiresIn ?? 900,
                TokenType = "Bearer",
                User = new
                {
                    id = result.User?.Id,
                    email = result.User?.Email,
                    firstName = result.User?.FirstName,
                    lastName = result.User?.LastName,
                    role = result.User?.Role,
                    isEmailVerified = result.User?.IsEmailVerified
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "[SignIn Error] ❌ Erreur durant la connexion\n" +
                "Email: {Email}\n" +
                "Exception: {ExceptionMessage}\n" +
                "StackTrace: {StackTrace}",
                request.Email,
                ex.Message,
                ex.StackTrace
            );
            return StatusCode(500, new { error = "An error occurred during login", details = ex.Message });
        }
    }

    /// <summary>
    /// Verify email with verification code
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VerifyEmailResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequestDto request)
    {
        if (request.UserId <= 0 || string.IsNullOrWhiteSpace(request.VerificationCode))
        {
            return BadRequest(new { error = "User ID and verification code are required" });
        }

        try
        {
            _logger.LogInformation("VerifyEmail attempt for user: {UserId}", request.UserId);

            var result = await _customAuthService.VerifyEmailAsync(request.UserId, request.VerificationCode);

            if (!result.Success)
            {
                _logger.LogWarning("VerifyEmail failed for user: {UserId}. Message: {Message}",
                    request.UserId, result.Message);
                return BadRequest(new { error = result.Message, details = result.Errors });
            }

            _logger.LogInformation("Email verified for user: {UserId}", request.UserId);

            return Ok(new VerifyEmailResponse
            {
                Message = result.Message,
                IsVerified = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email for user: {UserId}", request.UserId);
            return StatusCode(500, new { error = "An error occurred during verification", details = ex.Message });
        }
    }

    /// <summary>
    /// Resend verification code to email
    /// </summary>
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendVerificationRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required" });
        }

        try
        {
            _logger.LogInformation("Resend verification attempt for email: {Email}", request.Email);

            var result = await _customAuthService.ResendVerificationCodeAsync(request.Email);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to resend verification for email: {Email}", request.Email);
                return BadRequest(new { error = result.Message, details = result.Errors });
            }

            _logger.LogInformation("Verification code resent for email: {Email}", request.Email);
            return Ok(new { message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification for email: {Email}", request.Email);
            return StatusCode(500, new { error = "An error occurred", details = ex.Message });
        }
    }

    /// <summary>
    /// Request password reset
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "Email is required" });
        }

        try
        {
            _logger.LogInformation("Forgot password request for email: {Email}", request.Email);

            var result = await _customAuthService.ForgotPasswordAsync(request.Email);

            _logger.LogInformation("Forgot password email sent for: {Email}", request.Email);
            // Always return success to not reveal if email exists
            return Ok(new { message = "If the email exists, you will receive a password reset link" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing forgot password for email: {Email}", request.Email);
            return StatusCode(500, new { error = "An error occurred", details = ex.Message });
        }
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ResetToken) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Reset token and new password are required" });
        }

        try
        {
            _logger.LogInformation("Reset password request");

            var result = await _customAuthService.ResetPasswordAsync(request.ResetToken, request.NewPassword);

            if (!result.Success)
            {
                _logger.LogWarning("Reset password failed. Message: {Message}", result.Message);
                return BadRequest(new { error = result.Message, details = result.Errors });
            }

            _logger.LogInformation("Password reset successful");
            return Ok(new { message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password");
            return StatusCode(500, new { error = "An error occurred", details = ex.Message });
        }
    }

    /// <summary>
    /// Refresh access token
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RefreshTokenResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { error = "Refresh token is required" });
        }

        try
        {
            _logger.LogInformation("RefreshToken request");

            var result = await _customAuthService.RefreshAccessTokenAsync(request.RefreshToken);

            if (!result.Success)
            {
                _logger.LogWarning("RefreshToken failed. Message: {Message}", result.Message);
                return Unauthorized(new { error = result.Message, details = result.Errors });
            }

            _logger.LogInformation("Token refreshed successfully");
            return Ok(new RefreshTokenResponse
            {
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                ExpiresIn = result.ExpiresIn ?? 900,
                TokenType = "Bearer"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return StatusCode(500, new { error = "An error occurred", details = ex.Message });
        }
    }

    /// <summary>
    /// Logout - revoke refresh token
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto request)
    {
        try
        {
            _logger.LogInformation("Logout request");

            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                await _customAuthService.LogoutAsync(request.RefreshToken);
            }

            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { error = "An error occurred", details = ex.Message });
        }
    }

    /// <summary>
    /// Change password
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Current and new password are required" });
        }

        try
        {
            // Get user ID from JWT claim
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { error = "Invalid user ID in token" });
            }

            _logger.LogInformation("Change password request for user: {UserId}", userId);

            var success = await _customAuthService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);

            if (!success)
            {
                _logger.LogWarning("Change password failed for user: {UserId}", userId);
                return BadRequest(new { error = "Failed to change password. Invalid current password?" });
            }

            _logger.LogInformation("Password changed for user: {UserId}", userId);
            return Ok(new { message = "Password changed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, new { error = "An error occurred", details = ex.Message });
        }
    }

    /// <summary>
    /// Get current user info
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserResponse), 200)]
    [ProducesResponseType(401)]
    public IActionResult GetCurrentUser()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var emailClaim = User.FindFirst(ClaimTypes.Email)?.Value;
            var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized(new { error = "Invalid token" });
            }

            return Ok(new CurrentUserResponse
            {
                Id = int.Parse(userIdClaim),
                Email = emailClaim,
                Role = roleClaim ?? "student"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current user");
            return StatusCode(500, new { error = "An error occurred" });
        }
    }

    /// <summary>
    /// Endpoint de diagnostic - Vérifier les claims du JWT
    /// </summary>
    [HttpGet("diagnostic/claims")]
    [Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public IActionResult GetClaimsDiagnostic()
    {
        try
        {
            var claims = User.Claims.Select(c => new
            {
                c.Type,
                c.Value
            }).ToList();

            var userIdClaim = User.FindFirst("user_id")?.Value;
            var nameIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var emailClaim = User.FindFirst(ClaimTypes.Email)?.Value;

            return Ok(new
            {
                success = true,
                allClaims = claims,
                diagnostics = new
                {
                    hasUserIdClaim = !string.IsNullOrEmpty(userIdClaim),
                    userIdValue = userIdClaim,
                    hasNameIdClaim = !string.IsNullOrEmpty(nameIdClaim),
                    nameIdValue = nameIdClaim,
                    hasEmailClaim = !string.IsNullOrEmpty(emailClaim),
                    emailValue = emailClaim,
                    totalClaims = claims.Count,
                    isAuthenticated = User.Identity?.IsAuthenticated ?? false,
                    authenticationType = User.Identity?.AuthenticationType
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting claims diagnostic");
            return StatusCode(500, new { error = "An error occurred", details = ex.Message });
        }
    }
}

