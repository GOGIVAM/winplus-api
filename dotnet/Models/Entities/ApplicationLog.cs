using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class ApplicationLog
{
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string Level { get; set; } = "Error"; // Error | Warning | Info

    [MaxLength(200)]
    public string Category { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }

    public string? StackTrace { get; set; }

    [MaxLength(500)]
    public string? RequestPath { get; set; }

    public int? UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsResolved { get; set; } = false;

    public DateTime? ResolvedAt { get; set; }
}
