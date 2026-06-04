using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface ICertificateService
{
    Task<CertificateDto> GenerateCertificateAsync(int userId, int enrollmentId);
    Task<CertificateDto> GetCertificateAsync(int certificateId, int userId);
    Task<List<CertificateDto>> GetUserCertificatesAsync(int userId);
    Task<CertificateVerificationDto> VerifyCertificateAsync(string verificationCode);
    Task<List<CertificateDto>> GetSubjectCertificatesAsync(int subjectId);
}

public class CertificateService : ICertificateService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CertificateService> _logger;

    public CertificateService(ApplicationDbContext context, ILogger<CertificateService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CertificateDto> GenerateCertificateAsync(int userId, int enrollmentId)
    {
        try
        {
            // Get enrollment and verify completion
            var enrollment = await _context.Enrollments
                .Include(e => e.User)
                .Include(e => e.Subject)
                .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.UserId == userId);

            if (enrollment == null)
            {
                throw new KeyNotFoundException("Enrollment not found");
            }

            // Verify course is completed
            if (!enrollment.IsCompleted || enrollment.ProgressPercentage < 100)
            {
                throw new InvalidOperationException($"Course not completed. Progress: {enrollment.ProgressPercentage}%");
            }

            // Check if certificate already exists
            var existing = await _context.Certificates
                .FirstOrDefaultAsync(c => c.EnrollmentId == enrollmentId);

            if (existing != null)
            {
                _logger.LogInformation("Certificate already exists for enrollment {EnrollmentId}", enrollmentId);
                return MapToDto(existing, enrollment.User, enrollment.Subject);
            }

            // Generate unique certificate number and verification code
            var certificateNumber = $"CERT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";
            var verificationCode = Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper();

            var certificate = new Certificate
            {
                UserId = userId,
                SubjectId = enrollment.SubjectId,
                EnrollmentId = enrollmentId,
                CertificateNumber = certificateNumber,
                IssuedAt = DateTime.UtcNow,
                CompletionDate = enrollment.CompletedAt ?? DateTime.UtcNow,
                Grade = null, // TODO: Calculate from quiz scores or other metrics
                VerificationCode = verificationCode
            };

            _context.Certificates.Add(certificate);
            await _context.SaveChangesAsync();

            // TODO: Generate PDF and upload to storage
            // For now, just set a placeholder URL
            certificate.FileUrl = $"/certificates/{certificateNumber}.pdf";
            await _context.SaveChangesAsync();

            _logger.LogInformation("Certificate {CertificateNumber} generated for user {UserId} - enrollment {EnrollmentId}",
                certificateNumber, userId, enrollmentId);

            return MapToDto(certificate, enrollment.User, enrollment.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating certificate for enrollment {EnrollmentId}", enrollmentId);
            throw;
        }
    }

    public async Task<CertificateDto> GetCertificateAsync(int certificateId, int userId)
    {
        try
        {
            var certificate = await _context.Certificates
                .Include(c => c.User)
                .Include(c => c.Subject)
                .FirstOrDefaultAsync(c => c.Id == certificateId && c.UserId == userId);

            if (certificate == null)
            {
                throw new KeyNotFoundException("Certificate not found");
            }

            return MapToDto(certificate, certificate.User, certificate.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certificate {CertificateId}", certificateId);
            throw;
        }
    }

    public async Task<List<CertificateDto>> GetUserCertificatesAsync(int userId)
    {
        try
        {
            var certificates = await _context.Certificates
                .Include(c => c.User)
                .Include(c => c.Subject)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.IssuedAt)
                .ToListAsync();

            return certificates.Select(c => MapToDto(c, c.User, c.Subject)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certificates for user {UserId}", userId);
            return new List<CertificateDto>();
        }
    }

    public async Task<CertificateVerificationDto> VerifyCertificateAsync(string verificationCode)
    {
        try
        {
            var certificate = await _context.Certificates
                .Include(c => c.User)
                .Include(c => c.Subject)
                .FirstOrDefaultAsync(c => c.VerificationCode == verificationCode);

            if (certificate == null)
            {
                return new CertificateVerificationDto
                {
                    IsValid = false
                };
            }

            return new CertificateVerificationDto
            {
                IsValid = true,
                CertificateNumber = certificate.CertificateNumber,
                StudentName = $"{certificate.User.FirstName} {certificate.User.LastName}",
                CourseName = certificate.Subject.Title,
                IssuedAt = certificate.IssuedAt,
                CompletionDate = certificate.CompletionDate,
                Grade = certificate.Grade
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying certificate with code {VerificationCode}", verificationCode);
            return new CertificateVerificationDto
            {
                IsValid = false
            };
        }
    }

    public async Task<List<CertificateDto>> GetSubjectCertificatesAsync(int subjectId)
    {
        try
        {
            var certificates = await _context.Certificates
                .Include(c => c.User)
                .Include(c => c.Subject)
                .Where(c => c.SubjectId == subjectId)
                .OrderByDescending(c => c.IssuedAt)
                .ToListAsync();

            return certificates.Select(c => MapToDto(c, c.User, c.Subject)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting certificates for subject {SubjectId}", subjectId);
            return new List<CertificateDto>();
        }
    }

    private CertificateDto MapToDto(Certificate certificate, User user, Subject subject)
    {
        return new CertificateDto
        {
            Id = certificate.Id,
            UserId = certificate.UserId,
            SubjectId = certificate.SubjectId,
            EnrollmentId = certificate.EnrollmentId,
            CertificateNumber = certificate.CertificateNumber,
            SubjectTitle = subject.Title,
            UserName = $"{user.FirstName} {user.LastName}",
            IssuedAt = certificate.IssuedAt,
            CompletionDate = certificate.CompletionDate,
            Grade = certificate.Grade,
            FileUrl = certificate.FileUrl,
            VerificationCode = certificate.VerificationCode
        };
    }
}
