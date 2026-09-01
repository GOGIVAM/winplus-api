using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class ConcoursEvent
{
    public int Id { get; set; }

    /// Slug identifiant le concours (ens, polytechnique, fmsb, enam, ...)
    [Required, MaxLength(50)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int Year { get; set; }

    public DateTime? RegistrationStartDate { get; set; }

    public DateTime? RegistrationEndDate { get; set; }

    public DateTime? ExamDate { get; set; }

    public DateTime? ResultsDate { get; set; }

    [MaxLength(300)]
    public string? Location { get; set; }

    /// Frais d'inscription en XAF (null = non précisé)
    public int? EnrollmentFeeXaf { get; set; }

    /// URL du portail officiel d'inscription
    [MaxLength(500)]
    public string? OfficialRegistrationUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
