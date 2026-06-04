using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// DTO for certificate response
/// </summary>
public class CertificateDto
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    public int SubjectId { get; set; }
    
    public int EnrollmentId { get; set; }
    
    public string CertificateNumber { get; set; }
    
    public string SubjectTitle { get; set; }
    
    public string UserName { get; set; }
    
    public DateTime IssuedAt { get; set; }
    
    public DateTime CompletionDate { get; set; }
    
    public decimal? Grade { get; set; }
    
    public string FileUrl { get; set; }
    
    public string VerificationCode { get; set; }
}

/// <summary>
/// DTO for certificate generation request
/// </summary>
public class GenerateCertificateRequest
{
    [Required]
    public int EnrollmentId { get; set; }
}

/// <summary>
/// DTO for certificate verification response
/// </summary>
public class CertificateVerificationDto
{
    public bool IsValid { get; set; }
    
    public string CertificateNumber { get; set; }
    
    public string StudentName { get; set; }
    
    public string CourseName { get; set; }
    
    public DateTime IssuedAt { get; set; }
    
    public DateTime CompletionDate { get; set; }
    
    public decimal? Grade { get; set; }
}
