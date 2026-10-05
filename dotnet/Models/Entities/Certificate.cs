using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Certificate entity - represents a course completion certificate
/// </summary>
public class Certificate
{
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    [Required]
    public int SubjectId { get; set; }
    
    /// <summary>
    /// Module 21 (décision §4.G) : nullable depuis que la relation est en
    /// SetNull plutôt qu'en Cascade — un certificat déjà émis survit à la
    /// désinscription (et même, en toute rigueur, à une suppression physique
    /// de l'inscription si elle survenait, bien que la désinscription soit
    /// désormais logique). <see cref="EnrollmentId"/> nul signifie « émis par
    /// une inscription depuis disparue », jamais « jamais émis ».
    /// </summary>
    public int? EnrollmentId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string CertificateNumber { get; set; }
    
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime CompletionDate { get; set; }
    
    [Column("FinalScore", TypeName = "decimal(5,2)")]
    public decimal? Grade { get; set; } // 0-100
    
    [Column("CertificateUrl")]
    [MaxLength(500)]
    public string? FileUrl { get; set; }

    [NotMapped]
    public string? VerificationCode { get; set; }
    
    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User User { get; set; }
    
    [ForeignKey(nameof(SubjectId))]
    public Subject Subject { get; set; }
    
    [ForeignKey(nameof(EnrollmentId))]
    public Enrollment Enrollment { get; set; }
}
