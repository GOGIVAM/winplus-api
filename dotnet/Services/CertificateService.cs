using Amazon.S3;
using Amazon.S3.Model;
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
    Task<List<CertificateDto>> GetAllCertificatesAsync(DateTime? from = null, DateTime? to = null);
    Task<CertificateDto> AdminIssueCertificateAsync(AdminIssueCertificateRequest request);
}

public class CertificateService : ICertificateService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CertificateService> _logger;
    private readonly IPdfService _pdfService;
    private readonly IConfiguration _configuration;

    public CertificateService(
        ApplicationDbContext context,
        ILogger<CertificateService> logger,
        IPdfService pdfService,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _pdfService = pdfService;
        _configuration = configuration;
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
                Grade = await ComputeGradeAsync(userId, enrollment.SubjectId),
                VerificationCode = verificationCode
            };

            _context.Certificates.Add(certificate);
            await _context.SaveChangesAsync();

            certificate.FileUrl = await GenerateAndUploadPdfAsync(certificate, enrollment.User!, enrollment.Subject!, certificateNumber);
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

    public async Task<List<CertificateDto>> GetAllCertificatesAsync(DateTime? from = null, DateTime? to = null)
    {
        try
        {
            var query = _context.Certificates
                .Include(c => c.User)
                .Include(c => c.Subject)
                .AsQueryable();

            if (from.HasValue) query = query.Where(c => c.IssuedAt >= from.Value);
            if (to.HasValue) query = query.Where(c => c.IssuedAt <= to.Value.AddDays(1));

            var certs = await query.OrderByDescending(c => c.IssuedAt).ToListAsync();
            return certs.Select(c => MapToDto(c, c.User, c.Subject)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all certificates");
            return new List<CertificateDto>();
        }
    }

    public async Task<CertificateDto> AdminIssueCertificateAsync(AdminIssueCertificateRequest request)
    {
        try
        {
            var enrollment = await _context.Enrollments
                .Include(e => e.User)
                .Include(e => e.Subject)
                .Where(e => e.UserId == request.UserId && e.SubjectId == request.SubjectId)
                .OrderByDescending(e => e.EnrolledAt)
                .FirstOrDefaultAsync();

            if (enrollment == null)
                throw new KeyNotFoundException($"No enrollment found for user {request.UserId} in subject {request.SubjectId}");

            // Check if already issued
            var existing = await _context.Certificates
                .Include(c => c.User)
                .Include(c => c.Subject)
                .FirstOrDefaultAsync(c => c.EnrollmentId == enrollment.Id);

            if (existing != null)
                return MapToDto(existing, existing.User, existing.Subject);

            var certNumber = $"CERT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
            var verCode = Guid.NewGuid().ToString("N")[..12].ToUpper();

            var cert = new Certificate
            {
                UserId = request.UserId,
                SubjectId = request.SubjectId,
                EnrollmentId = enrollment.Id,
                CertificateNumber = certNumber,
                IssuedAt = request.IssuedAt ?? DateTime.UtcNow,
                CompletionDate = request.CompletionDate ?? enrollment.CompletedAt ?? DateTime.UtcNow,
                Grade = request.Grade,
                VerificationCode = verCode,
                FileUrl = $"/certificates/{certNumber}.pdf"
            };

            _context.Certificates.Add(cert);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Admin issued certificate {CertNumber} for user {UserId}", certNumber, request.UserId);
            return MapToDto(cert, enrollment.User, enrollment.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in admin certificate issuance");
            throw;
        }
    }

    private async Task<decimal?> ComputeGradeAsync(int userId, int subjectId)
    {
        var avg = await _context.LearningHistories
            .Where(h => h.UserId == userId && h.SubjectId == subjectId
                     && (h.QuizScore != null || h.Score != null))
            .AverageAsync(h => (double?)(h.QuizScore ?? h.Score));
        return avg.HasValue ? (decimal?)Math.Round(avg.Value, 2) : null;
    }

    private async Task<string> GenerateAndUploadPdfAsync(
        Certificate cert, User user, Subject subject, string certNumber)
    {
        try
        {
            var pdfBytes = _pdfService.GenerateCertificate(cert, user, subject);
            const string BucketName = "winplus-bucket";
            var region = _configuration["AWS:Region"] ?? "us-east-1";
            var key = $"certificates/{certNumber}.pdf";

            var regionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
            using var s3 = new AmazonS3Client(regionEndpoint);
            using var stream = new MemoryStream(pdfBytes);
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = BucketName,
                Key         = key,
                InputStream = stream,
                ContentType = "application/pdf",
                CannedACL   = S3CannedACL.PublicRead,
            });

            var url = $"https://{BucketName}.s3.{region}.amazonaws.com/{key}";
            _logger.LogInformation("Certificate PDF uploaded: {Url}", url);
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload certificate PDF for {CertNumber}", certNumber);
            return $"/certificates/{certNumber}.pdf"; // fallback local path
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
