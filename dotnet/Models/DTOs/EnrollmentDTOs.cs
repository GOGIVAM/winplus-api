using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// Corps autorisé pour POST /api/enrollments (Module 17).
///
/// L'endpoint liait auparavant directement l'entité `Enrollment`, ce qui
/// permettait de fournir un `UserId` arbitraire (inscription au nom d'un
/// autre compte) et d'écrire au passage la progression, l'achèvement ou
/// l'adresse du certificat. Seul le contenu visé est désormais accepté :
/// l'utilisateur est déduit du jeton.
/// </summary>
public class EnrollRequest
{
    [Range(1, int.MaxValue)]
    public int SubjectId { get; set; }
}

/// <summary>
/// DTO for enrollment progress response
/// </summary>
public class EnrollmentProgressDto
{
    public int EnrollmentId { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public string SubjectTitle { get; set; }
    
    public decimal ProgressPercentage { get; set; }
    
    public bool IsCompleted { get; set; }
    
    public DateTime EnrolledAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    public int TotalContents { get; set; }
    
    public int CompletedContents { get; set; }
    
    public DateTime? LastAccessedAt { get; set; }
}
