namespace Backend.Models.Entities;

/// <summary>
/// Enrollment entity - represents a user's enrollment in a course
/// </summary>
public class Enrollment
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? CompletedAt { get; set; }
    
    public decimal ProgressPercentage { get; set; } = 0;
    
    public bool IsCompleted { get; set; } = false;

    public string? CertificateUrl { get; set; }

    /// <summary>
    /// Suppression logique (Module 21, décision §4.G). Les colonnes existaient
    /// déjà en base depuis la migration AddUnenrollToEnrollment mais n'étaient
    /// pas mappées : le service effectuait une suppression physique, qui
    /// détruisait le certificat déjà obtenu (relation en cascade). La
    /// désinscription est désormais réversible en base et le certificat en
    /// est rendu indépendant (voir ApplicationDbContext, relation Certificate).
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    public DateTime? UnenrolledAt { get; set; }

    public string? UnenrollReason { get; set; }

    // Navigation properties
    public User? User { get; set; }
    
    public Subject? Subject { get; set; }
    
    public Certificate? Certificate { get; set; }
}
