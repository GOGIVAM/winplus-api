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
    
    [Required]
    public int EnrollmentId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string CertificateNumber { get; set; }
    
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime CompletionDate { get; set; }
    
    [Column(TypeName = "decimal(5,2)")]
    public decimal? Grade { get; set; } // 0-100
    
    [MaxLength(500)]
    public string FileUrl { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string VerificationCode { get; set; }
    
    // Navigation properties
    [ForeignKey(nameof(UserId))]
    public User User { get; set; }
    
    [ForeignKey(nameof(SubjectId))]
    public Subject Subject { get; set; }
    
    [ForeignKey(nameof(EnrollmentId))]
    public Enrollment Enrollment { get; set; }
}
