using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class FocusSession
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    /// Durée planifiée en secondes (25*60, 50*60, ou custom)
    public int PlannedDurationSeconds { get; set; }

    /// Durée réelle effectuée en secondes
    public int? ActualDurationSeconds { get; set; }

    /// Libellé affiché dans le bandeau (ex: "Révision Maths")
    [MaxLength(200)]
    public string? Label { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;
}
