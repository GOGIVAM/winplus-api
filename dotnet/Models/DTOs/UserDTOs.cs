using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO for requesting email change
/// </summary>
public class ChangeEmailRequest
{
    [Required]
    [EmailAddress]
    public string NewEmail { get; set; }
    
    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } // Password confirmation for security
}

/// <summary>
/// DTO for confirming email change with verification code
/// </summary>
public class ConfirmEmailChangeRequest
{
    [Required]
    [StringLength(10, MinimumLength = 6)]
    public string VerificationCode { get; set; }
}
